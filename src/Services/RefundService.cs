using System.Data;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;
using Services.Credits;

namespace Services;

/// <summary>
/// #281/#282: both halves of refunds, end to end.
/// <para>
/// <b>The method decides what moves.</b> A credit refund posts to the ledger at approval, so the
/// learner spends the compensation without waiting for anyone. A money refund moves nothing here: the
/// accountant pays by bank transfer, and <see cref="SettleRefundAsync"/> records that the transfer
/// happened. That is why settlement, not approval, is when money counts against revenue (ADR-0014):
/// approval is a decision to pay, and a decision is not a payment.
/// </para>
/// <para>
/// <b>Approval posts the credits, not the request.</b> A request is a promise; approval is the
/// decision to keep it. If the request posted, the requester would be spending the company's credits
/// before anyone agreed to, and a rejected refund would need a claw-back entry - which the append-only
/// ledger does not offer. So the ledger call sits in <see cref="ApproveRefundAsync"/>, inside the same
/// serializable transaction that flips the status: the learner's balance moves before the request
/// returns, and a failure anywhere rolls both back together.
/// </para>
/// <para>
/// <b>The ceiling lives here and the method/amount split lives in the schema.</b> A CHECK cannot read
/// another table, so the schema can prove that a credit refund carries a credit count and no money
/// (<c>CK_Refunds_MethodOwnsAmount</c>) but cannot know how much of the linked order was granted or
/// paid. That is a query, so it is the service's fact. It is enforced on the request, which is the only
/// moment a refund can be created; <c>requested</c> rows count against the ceiling, so two requests
/// that would together exceed it cannot both exist - a promise already spends the order, and counting
/// only approved rows would let a queue of requests each fit on its own and overshoot in total.
/// <c>Refunds</c> carries no UPDATE guard, so approval must not re-check: it would re-count the row it
/// is approving and refuse itself.
/// </para>
/// </summary>
public sealed class RefundService : IRefundService
{
    /// <summary>Statuses that count against a ceiling. A rejected refund returned nothing, so it
    /// holds nothing back.</summary>
    private static readonly string[] LiveStatuses =
    [
        RefundStatus.Requested.ToCode(),
        RefundStatus.Approved.ToCode(),
        RefundStatus.Settled.ToCode(),
    ];

    private readonly CompanionDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ICreditLedger _creditLedger;
    private readonly RefundOptions _options;

    public RefundService(
        CompanionDbContext db,
        TimeProvider? clock = null,
        RefundOptions? options = null,
        ICreditLedger? creditLedger = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
        _options = options ?? new RefundOptions();
        _creditLedger = creditLedger ?? new CreditLedger(db, _clock);
    }

    public async Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var requestedCount = await _db.Refunds
            .AsNoTracking()
            .CountAsync(r => r.Status == RefundStatus.Requested.ToCode(), cancellationToken);
        var approvedCount = await _db.Refunds
            .AsNoTracking()
            .CountAsync(r => r.Status == RefundStatus.Approved.ToCode(), cancellationToken);
        var rejectedCount = await _db.Refunds
            .AsNoTracking()
            .CountAsync(r => r.Status == RefundStatus.Rejected.ToCode(), cancellationToken);

        // The one figure on this screen that is about value rather than volume: credits that actually
        // returned to learners. A requested or rejected refund moved nothing, so it is excluded.
        var creditsRefunded = await _db.Refunds
            .AsNoTracking()
            .Where(r => r.Status == RefundStatus.Approved.ToCode() && r.Method == RefundMethod.Credit.ToCode())
            .SumAsync(r => r.CreditCount ?? 0, cancellationToken);

        IQueryable<Refund> query = _db.Refunds.AsNoTracking().Include(r => r.User).Include(r => r.Order);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmed = q.Trim().ToLowerInvariant();
            var idString = trimmed.StartsWith("#r-") ? trimmed[3..]
                : trimmed.StartsWith("r-") ? trimmed[2..]
                : trimmed.StartsWith('#') ? trimmed[1..]
                : trimmed;
            var isId = int.TryParse(idString, out var searchId);

            query = query.Where(r =>
                (isId && r.RefundId == searchId) ||
                r.User.Email.ToLower().Contains(trimmed) ||
                (r.User.FullName != null && r.User.FullName.ToLower().Contains(trimmed)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase)
            && EnumCodes.TryParse<RefundStatus>(status.Trim(), out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus.ToCode());
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AdminRefundsSummaryDto(
            items.Select(ToListItem).ToList(),
            totalCount,
            page,
            pageSize,
            requestedCount,
            approvedCount,
            rejectedCount,
            creditsRefunded);
    }

    public async Task<AdminRefundDetailDto?> GetAdminRefundDetailAsync(
        int refundId,
        CancellationToken cancellationToken = default)
    {
        var refund = await _db.Refunds
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Order)
            .Include(r => r.RequestedByUser)
            .Include(r => r.DecidedByUser)
            .Include(r => r.SettledByUser)
            .Include(r => r.CreditTransaction)
            .SingleOrDefaultAsync(r => r.RefundId == refundId, cancellationToken);

        return refund is null ? null : ToDetail(refund);
    }

    public Task<AdminRefundDetailDto> RequestCreditRefundAsync(
        int staffUserId,
        int learnerUserId,
        int creditCount,
        string reason,
        int? orderId = null,
        int? complaintId = null,
        CancellationToken cancellationToken = default) =>
        RequestRefundAsync(
            staffUserId,
            new RequestRefundRequest(learnerUserId, RefundMethodDto.Credit, creditCount, reason, orderId, complaintId),
            cancellationToken);

    public async Task<AdminRefundDetailDto> RequestRefundAsync(
        int staffUserId,
        RequestRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        if (staffUserId <= 0)
        {
            throw new RefundException(ApiError.Codes.Validation, "Invalid staff user ID.", 400);
        }

        var reason = ValidateReason(request.Reason);

        // The method owns the value column (CK_Refunds_MethodOwnsAmount). Validate the half the method
        // names, and refuse a figure in the other column rather than dropping it on insert.
        decimal? amount = null;
        var creditCount = 0;
        if (request.Method == RefundMethodDto.Money)
        {
            amount = ValidateAmount(request.Amount);
            if (request.CreditCount > 0)
            {
                throw new RefundException(
                    ApiError.Codes.RefundAmountInvalid,
                    "A money refund carries an amount, not a credit count.",
                    400);
            }
        }
        else
        {
            creditCount = ValidateCreditCount(request.CreditCount);
            if (request.Amount is > 0)
            {
                throw new RefundException(
                    ApiError.Codes.RefundAmountInvalid,
                    "A credit refund carries a credit count, not an amount of money.",
                    400);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var learner = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == request.UserId && !u.IsDeleted, cancellationToken);
        if (learner is null)
        {
            throw new RefundException(ApiError.Codes.UserNotFound, $"User {request.UserId} was not found.", 404);
        }

        await AssertCeilingAsync(
            request.UserId, request.OrderId, request.Method, amount ?? 0m, creditCount, cancellationToken);
        await AssertSubjectBelongsToLearnerAsync(request.UserId, request.OrderId, request.ComplaintId, cancellationToken);

        var refund = new Refund
        {
            UserId = request.UserId,
            ComplaintId = request.ComplaintId,
            OrderId = request.OrderId,
            Method = request.Method == RefundMethodDto.Money ? RefundMethod.Money.ToCode() : RefundMethod.Credit.ToCode(),
            Reason = reason,
            Status = RefundStatus.Requested.ToCode(),
            Amount = amount,
            CreditCount = request.Method == RefundMethodDto.Money ? null : creditCount,
            RequestedByUserId = staffUserId,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.Refunds.Add(refund);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (await GetAdminRefundDetailAsync(refund.RefundId, cancellationToken))!;
    }

    public async Task<AdminRefundDetailDto> ApproveRefundAsync(
        int staffUserId,
        int refundId,
        CancellationToken cancellationToken = default)
    {
        if (staffUserId <= 0)
        {
            throw new RefundException(ApiError.Codes.Validation, "Invalid staff user ID.", 400);
        }

        // The status flip and, for a credit refund, the ledger call share one serializable
        // transaction: a second approval racing this one either waits and then reads 'approved', or is
        // refused by the deadlock victim. A read-then-write without the transaction would let both
        // through, and the learner would be paid twice.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var refund = await _db.Refunds.SingleOrDefaultAsync(r => r.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            throw new RefundNotFoundException(refundId);
        }

        AssertUndecided(refund);

        // A money refund is approved by a decision only: nothing moves until the accountant settles it.
        // So the ledger is called on the credit arm and nowhere else - a money refund that posted
        // credits would pay the learner twice, once in each currency.
        if (refund.Method == RefundMethod.Credit.ToCode())
        {
            var creditCount = refund.CreditCount ?? 0;

            var note = $"Refund #{refund.RefundId}: {refund.Reason}";
            var receipt = await _creditLedger.GrantRefundCreditsAsync(
                refund.UserId,
                staffUserId,
                creditCount,
                note,
                cancellationToken);

            refund.CreditTxnId = receipt.TxnId;
        }

        refund.Status = RefundStatus.Approved.ToCode();
        refund.DecidedByUserId = staffUserId;
        refund.DecidedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (await GetAdminRefundDetailAsync(refundId, cancellationToken))!;
    }

    public async Task<AdminRefundDetailDto> RejectRefundAsync(
        int staffUserId,
        int refundId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (staffUserId <= 0)
        {
            throw new RefundException(ApiError.Codes.Validation, "Invalid staff user ID.", 400);
        }

        var rejectionReason = ValidateReason(reason);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var refund = await _db.Refunds.SingleOrDefaultAsync(r => r.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            throw new RefundNotFoundException(refundId);
        }

        AssertUndecided(refund);

        // The refusal is on record: the original reason stays, the rejection reason is appended, so
        // "why was this refused" survives without a second column the schema does not have.
        refund.Reason = $"{refund.Reason} | Rejected: {rejectionReason}";
        refund.Status = RefundStatus.Rejected.ToCode();
        refund.DecidedByUserId = staffUserId;
        refund.DecidedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (await GetAdminRefundDetailAsync(refundId, cancellationToken))!;
    }

    /// <summary>
    /// #282: the accountant records that the transfer happened. <b>This is the moment money counts</b>
    /// against revenue (ADR-0014), so it stamps who and when and moves nothing else: no credits, no
    /// order change. It refuses a credit refund before the schema can, because
    /// <c>CK_Refunds_SettledIsMoney</c> would answer a caller with a 500 for something they can fix
    /// by reading the message.
    /// </summary>
    public async Task<AdminRefundDetailDto> SettleRefundAsync(
        int staffUserId,
        int refundId,
        CancellationToken cancellationToken = default)
    {
        if (staffUserId <= 0)
        {
            throw new RefundException(ApiError.Codes.Validation, "Invalid staff user ID.", 400);
        }

        // Serialized for the same reason approval is: two accountants clicking settle must not both
        // stamp, and the second must read 'settled' rather than overwrite the first one's date.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var refund = await _db.Refunds.SingleOrDefaultAsync(r => r.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            throw new RefundNotFoundException(refundId);
        }

        if (refund.Method != RefundMethod.Money.ToCode())
        {
            throw new RefundException(
                ApiError.Codes.RefundCreditCannotSettle,
                "A credit refund cannot be settled: the credits landed when it was approved, so there is no transfer to record.",
                400);
        }

        if (refund.Status != RefundStatus.Approved.ToCode())
        {
            throw new RefundException(
                ApiError.Codes.RefundNotApproved,
                $"Refund {refund.RefundId} is {refund.Status}; only an approved money refund can be settled.",
                400);
        }

        refund.Status = RefundStatus.Settled.ToCode();
        refund.SettledByUserId = staffUserId;
        refund.SettledAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (await GetAdminRefundDetailAsync(refundId, cancellationToken))!;
    }

    /// <summary>
    /// The ticket's "decided once" rule, in the same shape <see cref="ComplaintService"/> uses for an
    /// already-resolved complaint. Called while the serializable transaction holds the row, so the
    /// read and the write cannot disagree.
    /// </summary>
    private static void AssertUndecided(Refund refund)
    {
        if (refund.Status != RefundStatus.Requested.ToCode())
        {
            throw new RefundException(
                ApiError.Codes.RefundAlreadyDecided,
                $"Refund {refund.RefundId} has already been decided and cannot be decided again.",
                400);
        }
    }

    /// <summary>
    /// The ceiling, in the one shape both methods share. What differs is only which column the value
    /// lives in and what caps it: credits are capped by what the order <b>granted</b>, money by what it
    /// was <b>paid</b>. A second hand-written count of "already refunded against this order" is how two
    /// screens end up disagreeing about one order - the defect #279 was.
    /// </summary>
    private async Task AssertCeilingAsync(
        int userId,
        int? orderId,
        RefundMethodDto method,
        decimal amount,
        int creditCount,
        CancellationToken cancellationToken)
    {
        var requested = method == RefundMethodDto.Money ? amount : creditCount;

        if (orderId is null)
        {
            var ceiling = _options.OrderlessCeiling > 0 ? _options.OrderlessCeiling : new RefundOptions().OrderlessCeiling;
            if (requested > ceiling)
            {
                throw new RefundException(
                    ApiError.Codes.RefundExceedsCeiling,
                    method == RefundMethodDto.Money
                        ? $"A money refund with no order behind it may return at most {ceiling:N0} dong; {amount:N0} was requested."
                        : $"A refund with no order behind it may return at most {ceiling} credits; {creditCount} was requested.",
                    400);
            }

            return;
        }

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Package)
            .SingleOrDefaultAsync(o => o.OrderId == orderId.Value, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            throw new RefundException(
                ApiError.Codes.RefundInvalidSubject,
                $"Order {orderId} was not found for learner {userId}.",
                400);
        }

        // GrantPurchaseAsync only credits a paid order and only a paid order moved money, so an unpaid
        // order's ceiling is zero in both units rather than the package's sticker value.
        var paid = order.Status == OrderStatus.Paid.ToCode();
        var cap = method == RefundMethodDto.Money
            ? (paid ? order.Amount : 0m)
            : (paid ? order.Package.Credits : 0m);

        var alreadyRefunded = await AlreadyRefundedAsync(_db, orderId.Value, method, cancellationToken);

        if (alreadyRefunded + requested > cap)
        {
            throw new RefundException(
                ApiError.Codes.RefundExceedsOrderCeiling,
                method == RefundMethodDto.Money
                    ? $"Order {order.OrderCode} was paid {order.Amount:N0} dong and {alreadyRefunded:N0} dong are already refunded against it, so {amount:N0} more cannot be returned."
                    : $"Order {order!.OrderCode} granted {cap:N0} credits and {alreadyRefunded:N0} are already refunded against it, so {creditCount} more cannot be returned.",
                400);
        }
    }

    /// <summary>
    /// How much of one order is already spoken for, in the unit the method names. Counts the live
    /// statuses only: a rejected refund returned nothing, so it releases its claim.
    /// </summary>
    private static Task<decimal> AlreadyRefundedAsync(
        CompanionDbContext db,
        int orderId,
        RefundMethodDto method,
        CancellationToken cancellationToken)
    {
        var methodCode = method == RefundMethodDto.Money ? RefundMethod.Money.ToCode() : RefundMethod.Credit.ToCode();
        var live = db.Refunds
            .AsNoTracking()
            .Where(r => r.OrderId == orderId
                && r.Method == methodCode
                && LiveStatuses.Contains(r.Status));

        return method == RefundMethodDto.Money
            ? live.SumAsync(r => r.Amount ?? 0m, cancellationToken)
            : live.SumAsync(r => (decimal)(r.CreditCount ?? 0), cancellationToken);
    }

    /// <summary>The composite FKs prove ownership in the database; this states it as a domain refusal
    /// with a readable code, before EF turns it into a 500.</summary>
    private async Task AssertSubjectBelongsToLearnerAsync(
        int userId,
        int? orderId,
        int? complaintId,
        CancellationToken cancellationToken)
    {
        if (complaintId is null)
        {
            return;
        }

        var owned = await _db.Complaints
            .AsNoTracking()
            .AnyAsync(c => c.ComplaintId == complaintId.Value && c.UserId == userId, cancellationToken);
        if (!owned)
        {
            throw new RefundException(
                ApiError.Codes.RefundInvalidSubject,
                $"Complaint {complaintId} was not found for learner {userId}.",
                400);
        }
    }

    private static string ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RefundException(ApiError.Codes.RefundReasonRequired, "A reason is required on every refund.", 400);
        }

        var trimmed = reason.Trim();
        // Reason is NVARCHAR(400); a longer string would be truncated by the insert rather than refused.
        if (trimmed.Length > 400)
        {
            throw new RefundException(ApiError.Codes.RefundReasonRequired, "A refund reason cannot exceed 400 characters.", 400);
        }

        return trimmed;
    }

    private static int ValidateCreditCount(int creditCount)
    {
        if (creditCount <= 0)
        {
            throw new RefundException(
                ApiError.Codes.RefundCreditCountInvalid,
                "A credit refund must return at least one credit.",
                400);
        }

        return creditCount;
    }

    /// <summary>
    /// <c>Amount</c> is <c>DECIMAL(12,2)</c>, and the schema's own <c>CK_Refunds_MethodOwnsAmount</c>
    /// requires it above zero. Checked here so the caller gets a code instead of a constraint error.
    /// A third decimal place is refused rather than rounded: the stored figure must be the figure the
    /// requester wrote, and rounding it would pay a different amount than the one on the form.
    /// </summary>
    private static decimal ValidateAmount(decimal? amount)
    {
        if (amount is null or <= 0m)
        {
            throw new RefundException(
                ApiError.Codes.RefundAmountInvalid,
                "A money refund must return more than zero dong.",
                400);
        }

        if (decimal.Round(amount.Value, 2) != amount.Value)
        {
            throw new RefundException(
                ApiError.Codes.RefundAmountInvalid,
                "A money refund is stored to two decimal places; the amount stated has a smaller unit.",
                400);
        }

        return amount.Value;
    }

    /// <summary>
    /// #283: <b>the export reads and nothing else.</b> It selects money refunds settled inside the range
    /// and shapes them for accounting; it writes no status and no timestamp onto a refund, so exporting
    /// twice yields the same rows and a settlement once recorded stays recorded.
    /// <para>
    /// <b>Settlement date, not request date (ADR-0014).</b> Settlement is the only moment money is treated
    /// as having left, so a refund requested in September and paid in October is October's payment. A
    /// range that selected on <c>CreatedAt</c> would put money in a month it never moved in, and the same
    /// refund could be paid twice by two periods that both claimed it.
    /// </para>
    /// <para>
    /// The window is cut at UTC+7 by <see cref="ReportingPeriod"/>, the one place a reporting period is
    /// cut, so a transfer stamped 23:30 Hanoi time on the 31st belongs to that day and not the next.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RefundExportRow>> GetRefundExportRowsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // A reversed range is the caller's mistake, not an empty month: swapping it would export a period
        // nobody asked for, so it is refused with the one code the screen already knows.
        if (to < from)
        {
            throw new RefundException(
                ApiError.Codes.Validation,
                $"The export range ends ({to:yyyy-MM-dd}) before it starts ({from:yyyy-MM-dd}).",
                400);
        }

        var start = StartOfDay(from);
        var end = StartOfDay(to.AddDays(1));

        var rows = await _db.Refunds
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Order)
            .Include(r => r.DecidedByUser)
            .Where(r => r.Method == RefundMethod.Money.ToCode()
                && r.Status == RefundStatus.Settled.ToCode()
                && r.SettledAt != null
                && r.SettledAt >= start
                && r.SettledAt < end)
            // A batch is paid in the order it happened, so the file reads chronologically instead of in
            // whatever order the identity column produced.
            .OrderBy(r => r.SettledAt)
            .ThenBy(r => r.RefundId)
            .ToListAsync(cancellationToken);

        return rows.Select(ToExportRow).ToList();
    }

    /// <summary>Midnight on <paramref name="day"/> on the accountant's calendar, as an instant.</summary>
    private static DateTimeOffset StartOfDay(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), ReportingPeriod.UtcOffset);

    private static RefundExportRow ToExportRow(Refund refund) => new(
        refund.RefundId,
        // The request date is read on the same calendar the settlement is, or one row would state two
        // different day boundaries.
        DateOnly.FromDateTime(ReportingPeriod.LocalInstant(refund.CreatedAt).DateTime),
        refund.SettledAt is { } settled
            ? DateOnly.FromDateTime(ReportingPeriod.LocalInstant(settled).DateTime)
            : null,
        refund.User.Email,
        refund.User.FullName ?? refund.User.Email,
        refund.Order?.OrderCode,
        ParseWireCode<RefundMethodDto>(refund.Method),
        refund.Amount,
        refund.CreditCount,
        refund.Reason,
        refund.DecidedByUser?.FullName ?? refund.DecidedByUser?.Email);

    private static AdminRefundListItemDto ToListItem(Refund refund) => new(
        refund.RefundId,
        refund.UserId,
        refund.User.FullName ?? refund.User.Email,
        refund.User.Email,
        ParseWireCode<RefundMethodDto>(refund.Method),
        ParseWireCode<RefundStatusDto>(refund.Status),
        refund.CreditCount ?? 0,
        refund.Reason,
        refund.OrderId,
        refund.CreatedAt,
        refund.DecidedAt,
        refund.Amount ?? 0m,
        refund.Order?.OrderCode,
        refund.SettledAt);

    private static AdminRefundDetailDto ToDetail(Refund refund) => new(
        refund.RefundId,
        refund.UserId,
        refund.User.FullName ?? refund.User.Email,
        refund.User.Email,
        ParseWireCode<RefundMethodDto>(refund.Method),
        ParseWireCode<RefundStatusDto>(refund.Status),
        refund.CreditCount ?? 0,
        refund.Reason,
        refund.OrderId,
        refund.Order?.OrderCode,
        refund.ComplaintId,
        refund.CreatedAt,
        refund.RequestedByUserId,
        refund.RequestedByUser?.FullName ?? refund.RequestedByUser?.Email,
        refund.DecidedByUserId,
        refund.DecidedByUser?.FullName ?? refund.DecidedByUser?.Email,
        refund.DecidedAt,
        refund.CreditTxnId,
        refund.CreditTransaction?.Delta,
        refund.Amount ?? 0m,
        refund.Order?.Amount,
        refund.SettledByUserId,
        refund.SettledByUser?.FullName ?? refund.SettledByUser?.Email,
        refund.SettledAt);
}
