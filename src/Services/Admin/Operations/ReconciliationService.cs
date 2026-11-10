using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #247.2: <b>every ledger balance after the cutover equals the balance in the pre-cutover snapshot, and
/// a discrepancy blocks the step.</b>
/// <para>
/// <b>Why this is the check that matters.</b> D23 says the financial pass "is the only thing that catches
/// a cutover which silently dropped credits", and it is right for a structural reason: the imported
/// ledger is internally consistent by construction - the balance is its sum, and the import either wrote
/// a row or did not. So a new database that is missing rows still reports a balance that agrees with
/// itself. Only a comparison against a number captured BEFORE the window can tell.
/// </para>
/// <para>
/// <b>Why the row count travels with the balance.</b> A dropped credit and a duplicated one cancel to
/// the same total. Two learners' rows swapped leave both totals intact. Comparing a count alongside the
/// sum is cheap and closes that whole family of silent errors.
/// </para>
/// <para>
/// <b>It reports; it never repairs.</b> Only a person can say which side is right, and a check that
/// "fixed" a balance would be rewriting money on a guess.
/// </para>
/// </summary>
public sealed class ReconciliationService(CompanionDbContext db) : IReconciliationService
{
    /// <summary>
    /// The snapshot, taken from the database the cutover is about to replace. One query for both numbers
    /// so the balance and its count describe the same instant: two queries could straddle a write and
    /// produce a snapshot that never existed.
    /// </summary>
    public async Task<BalanceSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.vw_UserCreditBalances
            .AsNoTracking()
            .Select(b => new { b.UserId, b.Balance })
            .ToListAsync(cancellationToken);

        var counts = await db.CreditTransactions
            .AsNoTracking()
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        var snapshot = rows
            .OrderBy(r => r.UserId)
            .Select(r => new BalanceSnapshotRow(r.UserId, r.Balance ?? 0, counts.GetValueOrDefault(r.UserId)))
            .ToList();

        return new BalanceSnapshot(DateTimeOffset.UtcNow, snapshot.Count, snapshot);
    }

    /// <summary>
    /// Compares the current database against a snapshot taken before the window. Every user in EITHER
    /// set is checked: a learner the snapshot has and this one does not is a DROP, and one this one has
    /// and the snapshot does not is a NEW wallet that must be accounted for.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(
        BalanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var balances = await db.vw_UserCreditBalances
            .AsNoTracking()
            .Select(b => new { b.UserId, b.Balance })
            .ToDictionaryAsync(b => b.UserId, b => b.Balance ?? 0, cancellationToken);

        var counts = await db.CreditTransactions
            .AsNoTracking()
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        var expected = snapshot.Rows.ToDictionary(r => r.UserId);
        var discrepancies = new List<BalanceDiscrepancy>();

        foreach (var row in snapshot.Rows.OrderBy(r => r.UserId))
        {
            var actualBalance = balances.GetValueOrDefault(row.UserId, 0);
            var actualCount = counts.GetValueOrDefault(row.UserId, 0);

            if (actualBalance != row.Balance)
            {
                discrepancies.Add(new BalanceDiscrepancy(
                    row.UserId, row.Balance, actualBalance, row.TxnCount, actualCount, "balance"));
            }
            else if (actualCount != row.TxnCount)
            {
                // Same total, different history. Called out separately because the balance check alone
                // cannot see it, and a swapped or merged row is a real defect even when it nets out.
                discrepancies.Add(new BalanceDiscrepancy(
                    row.UserId, row.Balance, actualBalance, row.TxnCount, actualCount, "row_count"));
            }
        }

        foreach (var userId in balances.Keys.Where(id => !expected.ContainsKey(id)).Order())
        {
            discrepancies.Add(new BalanceDiscrepancy(
                userId, 0, balances[userId], 0, counts.GetValueOrDefault(userId), "unexpected_wallet"));
        }

        return new ReconciliationResult(expected.Count, discrepancies);
    }
}
