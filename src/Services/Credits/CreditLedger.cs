using BusinessObjects;
using Contracts;
using static Contracts.EnumMapping;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Services.Credits;

/// <summary>
/// Deep module implementing the append-only credit ledger.
/// Encapsulates wallet balance views, "block before charge" overdraft checks,
/// composite target ownership, unique-index idempotency, and trigger error mappings.
/// </summary>
public sealed class CreditLedger : ICreditLedger
{
    private readonly CompanionDbContext _db;
    private readonly TimeProvider _clock;

    public CreditLedger(CompanionDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<int> GetBalanceAsync(int userId, CancellationToken cancellationToken = default)
    {
        var balance = await _db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        return balance;
    }

    public async Task<CreditReceipt> ChargeMockExamAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Charge amount must be positive.");
        }

        var attempt = await _db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Attempt {attemptId} was not found or does not belong to user {userId}.", 404);
        }

        if (attempt.Exam.Purpose != ExamPurpose.Mock.ToCode())
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Mock exam charge only applies to exams with purpose 'mock'.");
        }

        var reason = CreditReason.MockExamStart;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.AttemptId == attemptId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        if (balance < amount)
        {
            throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, "You do not have enough credits to start this mock exam.", 402);
        }

        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = -amount,
            AttemptId = attemptId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, -amount, balance - amount, now, cancellationToken);
    }

    public async Task<CreditReceipt> ChargeAiGradingAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Charge amount must be positive.");
        }

        var attempt = await _db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Attempt {attemptId} was not found or does not belong to user {userId}.", 404);
        }

        if (attempt.Exam.Purpose == ExamPurpose.Placement.ToCode())
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Placement tests are 100% free; no AI grading charge applies.");
        }

        var reason = CreditReason.AiGrading;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.AttemptId == attemptId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        if (balance < amount)
        {
            throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, "You do not have enough credits for AI grading.", 402);
        }

        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = -amount,
            AttemptId = attemptId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, -amount, balance - amount, now, cancellationToken);
    }

    public async Task<CreditReceipt> ChargeMentorBookingAsync(int userId, int bookingId, int priceCredits, CancellationToken cancellationToken = default)
    {
        if (priceCredits <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Booking price must be positive.");
        }

        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.Mentor)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);

        if (booking is null || booking.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Booking {bookingId} was not found or does not belong to user {userId}.", 404);
        }

        if (booking.Mentor.PriceCredits != priceCredits)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, $"Booking price {priceCredits} does not match mentor rate {booking.Mentor.PriceCredits}.");
        }

        var reason = CreditReason.MentorBooking;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        if (balance < priceCredits)
        {
            throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, "You do not have enough credits to book this mentor session.", 402);
        }

        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = -priceCredits,
            BookingId = bookingId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, -priceCredits, balance - priceCredits, now, cancellationToken);
    }

    public async Task<CreditReceipt> ChargePremiumPathAsync(int userId, int learnerPathId, int amount = 5, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Upgrade amount must be positive.");
        }

        var path = await _db.LearnerPaths
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LearnerPathId == learnerPathId, cancellationToken);

        if (path is null || path.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Learner path {learnerPathId} was not found or does not belong to user {userId}.", 404);
        }

        var reason = CreditReason.PremiumPath;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.LearnerPathId == learnerPathId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        if (balance < amount)
        {
            throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, $"You do not have enough credits to upgrade to premium ({amount} credits required).", 400);
        }

        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = -amount,
            LearnerPathId = learnerPathId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, -amount, balance - amount, now, cancellationToken);
    }

    public async Task<CreditReceipt> RefundBookingAsync(int userId, int bookingId, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Refund amount must be positive.");
        }

        var booking = await _db.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);

        if (booking is null || booking.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Booking {bookingId} was not found or does not belong to user {userId}.", 404);
        }

        if (booking.Status != BookingStatus.Cancelled.ToCode())
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Only cancelled bookings can be refunded.");
        }

        var reason = CreditReason.BookingCancel;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var originalCharge = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Reason == CreditReason.MentorBooking.ToCode(), cancellationToken);

        if (originalCharge is null || originalCharge.Delta != -amount)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Refund delta must exactly reverse a prior mentor booking charge.");
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = amount,
            BookingId = bookingId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, amount, balance + amount, now, cancellationToken);
    }

    public async Task<CreditReceipt> GrantPurchaseAsync(int userId, int orderId, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Purchase credits must be positive.");
        }

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Package)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            throw new CreditLedgerException(CreditLedgerException.TargetOwnerMismatch, $"Order {orderId} was not found or does not belong to user {userId}.", 404);
        }

        if (order.Status != OrderStatus.Paid.ToCode())
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Only paid orders may be credited to the wallet.");
        }

        if (order.Package.Credits != amount)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, $"Purchase delta {amount} must equal package credits {order.Package.Credits}.");
        }

        var reason = CreditReason.Purchase;
        var existing = await _db.CreditTransactions
            .FirstOrDefaultAsync(t => t.OrderId == orderId && t.Reason == reason.ToCode(), cancellationToken);
        if (existing is not null)
        {
            var curBalance = await GetBalanceAsync(userId, cancellationToken);
            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBalance, existing.CreatedAt);
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = userId,
            Reason = reason.ToCode(),
            Delta = amount,
            OrderId = orderId,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, reason, amount, balance + amount, now, cancellationToken);
    }

    public async Task<CreditReceipt> AdjustCreditsAsync(int targetUserId, int? adminUserId, int delta, string reason, CancellationToken cancellationToken = default)
    {
        if (delta == 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Adjustment delta cannot be zero.");
        }

        if (adminUserId is null)
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "AdminUserId is required for credit adjustments.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Reason is required for credit adjustments.");
        }

        var currentBalance = await GetBalanceAsync(targetUserId, cancellationToken);
        if (delta < 0 && currentBalance + delta < 0)
        {
            throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, "Credit deduction cannot exceed the user's current balance.");
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > 400)
        {
            trimmedReason = trimmedReason[..400];
        }

        var creditReason = delta > 0 ? CreditReason.AdminGrant : CreditReason.AdminRevoke;
        var now = _clock.GetUtcNow();
        var txn = new CreditTransaction
        {
            UserId = targetUserId,
            Reason = creditReason.ToCode(),
            Delta = delta,
            AdminUserId = adminUserId,
            Note = trimmedReason,
            CreatedAt = now
        };

        return await SaveTransactionAsync(txn, creditReason, delta, currentBalance + delta, now, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditLedgerEntryDto>> GetLedgerHistoryAsync(int userId, CancellationToken cancellationToken = default)
    {
        var ledger = await _db.CreditTransactions
            .AsNoTracking()
            .Include(t => t.Order)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return ledger.Select(t => new CreditLedgerEntryDto(
            TxnId: t.TxnId,
            Reason: ParseWireCode<CreditReasonDto>(t.Reason),
            Delta: t.Delta,
            Note: t.Note,
            CreatedAt: t.CreatedAt,
            OrderCode: t.Order?.OrderCode)).ToList();
    }

    private async Task<CreditReceipt> SaveTransactionAsync(
        CreditTransaction txn,
        CreditReason reason,
        int delta,
        int expectedNewBalance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            _db.CreditTransactions.Add(txn);
            await _db.SaveChangesAsync(cancellationToken);
            return new CreditReceipt(txn.TxnId, txn.UserId, reason, delta, expectedNewBalance, now);
        }
        catch (DbUpdateException ex)
        {
            _db.Entry(txn).State = EntityState.Detached;

            if (ex.InnerException is SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 2601 or 2627:
                        var existing = await FindDuplicateAsync(txn, cancellationToken);
                        if (existing is not null)
                        {
                            var curBal = await GetBalanceAsync(txn.UserId, cancellationToken);
                            return new CreditReceipt(existing.TxnId, existing.UserId, reason, existing.Delta, curBal, existing.CreatedAt);
                        }
                        throw new CreditLedgerException(CreditLedgerException.DuplicateCharge, "Duplicate ledger transaction detected.", 409, ex);

                    case 50020:
                        throw new CreditLedgerException(CreditLedgerException.InsufficientCredits, "Transaction would drive wallet balance negative.", 402, ex);

                    case 50021:
                        throw new CreditLedgerException(CreditLedgerException.NegativeBalanceBlocked, "CreditTransactions is append-only; rows cannot be deleted.", 400, ex);

                    case 50022:
                        throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Purchase transaction must reference a paid order with Delta equal to package credits.", 400, ex);

                    case 50028:
                        throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "CreditTransactions is append-only; a posted transaction cannot be rewritten.", 400, ex);

                    case 50035 or 50036:
                        throw new CreditLedgerException(CreditLedgerException.InvalidCharge, sqlEx.Message, 400, ex);

                    case 50038:
                        throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "This charge does not apply to the attempt's exam purpose.", 400, ex);
                }
            }

            throw new CreditLedgerException(CreditLedgerException.InvalidCharge, "Failed to persist ledger transaction.", 400, ex);
        }
    }

    private Task<CreditTransaction?> FindDuplicateAsync(CreditTransaction txn, CancellationToken cancellationToken)
    {
        if (txn.OrderId.HasValue)
        {
            return _db.CreditTransactions.FirstOrDefaultAsync(t => t.OrderId == txn.OrderId.Value, cancellationToken);
        }
        if (txn.BookingId.HasValue)
        {
            return _db.CreditTransactions.FirstOrDefaultAsync(t => t.BookingId == txn.BookingId.Value && t.Reason == txn.Reason, cancellationToken);
        }
        if (txn.LearnerPathId.HasValue)
        {
            return _db.CreditTransactions.FirstOrDefaultAsync(t => t.LearnerPathId == txn.LearnerPathId.Value, cancellationToken);
        }
        if (txn.AttemptId.HasValue)
        {
            return _db.CreditTransactions.FirstOrDefaultAsync(t => t.AttemptId == txn.AttemptId.Value && t.Reason == txn.Reason, cancellationToken);
        }
        return Task.FromResult<CreditTransaction?>(null);
    }
}
