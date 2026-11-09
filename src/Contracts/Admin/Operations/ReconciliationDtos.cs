namespace Contracts;

/// <summary>
/// #246.1: one learner's wallet, captured before the cutover window opens.
/// <para>
/// The snapshot is taken from the OLD database and compared against the NEW one after the import. It
/// carries the row count as well as the balance, because two ways of being wrong look identical from a
/// balance alone: a dropped credit and a duplicated one cancel each other out to the same total.
/// </para>
/// </summary>
public sealed record BalanceSnapshotRow(int UserId, int Balance, int TxnCount);

/// <summary>The pre-cutover snapshot. Taken once, before the window, and never edited afterwards.</summary>
public sealed record BalanceSnapshot(
    DateTimeOffset TakenAt,
    int UserCount,
    IReadOnlyList<BalanceSnapshotRow> Rows);

/// <summary>
/// #247.2: one learner whose post-cutover wallet disagrees with the snapshot. Reported, never repaired:
/// only a person can say which side is right, and guessing here would silently rewrite money.
/// </summary>
public sealed record BalanceDiscrepancy(
    int UserId,
    int ExpectedBalance,
    int ActualBalance,
    int ExpectedTxnCount,
    int ActualTxnCount,
    string Kind);

/// <summary>
/// #247.2: the financial pass. <see cref="Reconciled"/> false means the cutover step must not proceed.
/// <para>
/// This is the only check that catches a cutover which silently dropped credits, so it is not advisory:
/// the ticket says "a discrepancy blocks the step", and <see cref="BlockingReason"/> is what a script
/// prints before refusing.
/// </para>
/// </summary>
public sealed record ReconciliationResult(
    int UsersChecked,
    IReadOnlyList<BalanceDiscrepancy> Discrepancies)
{
    public bool Reconciled => Discrepancies.Count == 0;

    /// <summary>A sentence a cutover script can print verbatim. Null when reconciled.</summary>
    public string? BlockingReason => Reconciled
        ? null
        : $"{Discrepancies.Count} wallet(s) disagree with the pre-cutover snapshot "
          + $"across {UsersChecked} checked. The step must not proceed: a cutover that drops credits "
          + "cannot be detected afterwards, because the new balance agrees with the ledger it imported.";
}
