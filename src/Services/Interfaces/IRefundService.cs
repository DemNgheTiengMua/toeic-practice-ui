using Contracts;

namespace Services;

/// <summary>
/// #281: the credit half of refunds. A request is a record; approval is the moment value moves.
/// <para>
/// There is no delete and no reversal: the ledger is append-only, so a mistake is corrected by an
/// opposing entry rather than by removing the refund that made it. A money refund is not accepted
/// here yet (#282), and refusing it in the service - rather than hiding it in the screen - is what
/// makes the refusal testable.
/// </para>
/// </summary>
public interface IRefundService
{
    Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<AdminRefundDetailDto?> GetAdminRefundDetailAsync(
        int refundId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a refund request. Posts nothing: credits move on approval.</summary>
    Task<AdminRefundDetailDto> RequestRefundAsync(
        int staffUserId,
        RequestRefundRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The credit-only shorthand the admin screen uses. A money refund must go through
    /// <see cref="RequestRefundAsync"/> so it meets the refusal rather than a missing method.</summary>
    Task<AdminRefundDetailDto> RequestCreditRefundAsync(
        int staffUserId,
        int learnerUserId,
        int creditCount,
        string reason,
        int? orderId = null,
        int? complaintId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Approves and, for a credit refund, posts the credits in the same transaction.</summary>
    Task<AdminRefundDetailDto> ApproveRefundAsync(
        int staffUserId,
        int refundId,
        CancellationToken cancellationToken = default);

    /// <summary>Refuses a request with a recorded reason. Posts nothing.</summary>
    Task<AdminRefundDetailDto> RejectRefundAsync(
        int staffUserId,
        int refundId,
        string reason,
        CancellationToken cancellationToken = default);
}
