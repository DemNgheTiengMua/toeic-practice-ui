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
/// #281: the credit half of refunds, end to end.
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
/// (<c>CK_Refunds_MethodOwnsAmount</c>) but cannot know how many credits the linked order granted. That
/// is a query, so it is the service's fact. It is enforced on the request, which is the only moment a
/// refund can be created; <c>requested</c> rows count against the ceiling, so two requests that would
/// together exceed it cannot both exist. <c>Refunds</c> carries no UPDATE guard, so approval must not
/// re-check: it would re-count the row it is approving and refuse itself.
/// </para>
/// </summary>
public sealed class RefundService : IRefundService
{
    /// <summary>Statuses that count against a ceiling. A rejected refund returned nothing, so it
    /// holds no credits back.</summary>
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

        IQueryable<Refund> query = _db.Refunds.AsNoTracking().Include(r => r.User);

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

        // #282 owns money refunds. Refused here, not hidden in the screen, so the refusal has a test.
        if (request.Method != RefundMethodDto.Credit)
        {
            throw new RefundException(
                ApiError.Codes.RefundMoneyNotSupported,
                "A money refund cannot be created yet. Record a credit refund instead.",
                400);
        }

        var reason = ValidateReason(request.Reason);
        var creditCount = ValidateCreditCount(request.CreditCount);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var learner = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == request.UserId && !u.IsDeleted, cancellationToken);
        if (learner is null)
        {
            throw new RefundException(ApiError.Codes.UserNotFound, $"User {request.UserId} was not found.", 404);
        }

        await AssertCeilingAsync(request.UserId, request.OrderId, creditCount, excludeRefundId: null, cancellationToken);
        await AssertSubjectBelongsToLearnerAsync(request.UserId, request.OrderId, request.ComplaintId, cancellationToken);

        var refund = new Refund
        {
            UserId = request.UserId,
            ComplaintId = request.ComplaintId,
            OrderId = request.OrderId,
            Method = RefundMethod.Credit.ToCode(),
            Reason = reason,
            Status = RefundStatus.Requested.ToCode(),
            CreditCount = creditCount,
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

        // The ceiling check and the status flip share one serializable transaction: a second approval
        // racing this one either waits and then reads 'approved', or is refused by the deadlock
        // victim. A read-then-write without the transaction would let both through.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var refund = await _db.Refunds.SingleOrDefaultAsync(r => r.RefundId == refundId, cancellationToken);
        if (refund is null)
        {
            throw new RefundNotFoundException(refundId);
        }

        AssertUndecided(refund);

        if (refund.Method != RefundMethod.Credit.ToCode())
        {
            throw new RefundException(
                ApiError.Codes.RefundMoneyNotSupported,
                "A money refund is settled by accounting, not approved here.",
                400);
        }

        var creditCount = refund.CreditCount ?? 0;

        var note = $"Refund #{refund.RefundId}: {refund.Reason}";
        var receipt = await _creditLedger.GrantRefundCreditsAsync(
            refund.UserId,
            staffUserId,
            creditCount,
            note,
            cancellationToken);

        refund.CreditTxnId = receipt.TxnId;
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

    private async Task AssertCeilingAsync(
        int userId,
        int? orderId,
        int creditCount,
        int? excludeRefundId,
        CancellationToken cancellationToken)
    {
        if (orderId is null)
        {
            var ceiling = _options.OrderlessCeiling > 0 ? _options.OrderlessCeiling : new RefundOptions().OrderlessCeiling;
            if (creditCount > ceiling)
            {
                throw new RefundException(
                    ApiError.Codes.RefundExceedsCeiling,
                    $"A refund with no order behind it may return at most {ceiling} credits; {creditCount} was requested.",
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

        // GrantPurchaseAsync only credits a paid order, so an unpaid order granted nothing and its
        // ceiling is zero rather than the package's sticker value.
        var granted = order.Status == OrderStatus.Paid.ToCode() ? order.Package.Credits : 0;

        var alreadyRefunded = await _db.Refunds
            .AsNoTracking()
            .Where(r => r.OrderId == orderId.Value
                && r.Method == RefundMethod.Credit.ToCode()
                && LiveStatuses.Contains(r.Status)
                && (excludeRefundId == null || r.RefundId != excludeRefundId.Value))
            .SumAsync(r => r.CreditCount ?? 0, cancellationToken);

        if (alreadyRefunded + creditCount > granted)
        {
            throw new RefundException(
                ApiError.Codes.RefundExceedsOrderCeiling,
                $"Order {order!.OrderCode} granted {granted} credits and {alreadyRefunded} are already refunded against it, "
                + $"so {creditCount} more cannot be returned.",
                400);
        }
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
        refund.DecidedAt);

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
        refund.CreditTransaction?.Delta);
}
