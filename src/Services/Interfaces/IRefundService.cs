using Contracts;

namespace Services;

/// <summary>
/// #281/#282: both halves of refunds. A request is a record; approval is the moment a credit refund
/// moves value, and settlement is the moment a money refund's transfer has happened.
/// <para>
/// There is no delete and no reversal: the ledger is append-only, so a mistake is corrected by an
/// opposing entry rather than by removing the refund that made it. A credit refund terminates at
/// <c>approved</c>; only a money refund can reach <c>settled</c>, because only money leaves the
/// building by a transfer someone outside the product performs.
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

    /// <summary>Records a refund request of either method. Posts nothing: a credit refund's credits
    /// move on approval, and a money refund moves nothing here at all.</summary>
    Task<AdminRefundDetailDto> RequestRefundAsync(
        int staffUserId,
        RequestRefundRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The credit-only shorthand the admin screen uses.</summary>
    Task<AdminRefundDetailDto> RequestCreditRefundAsync(
        int staffUserId,
        int learnerUserId,
        int creditCount,
        string reason,
        int? orderId = null,
        int? complaintId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Approves and, for a credit refund only, posts the credits in the same transaction.
    /// A money refund's approval moves nothing.</summary>
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

    /// <summary>
    /// #282: the accountant records that a money refund was actually paid, stamping who and when. This
    /// is the moment money counts against revenue (ADR-0014). Refuses a credit refund, and refuses a
    /// money refund that has not been approved.
    /// </summary>
    Task<AdminRefundDetailDto> SettleRefundAsync(
        int staffUserId,
        int refundId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// #283: the rows accounting receives for a settling period, oldest settlement first. The range
    /// selects on <b>settlement</b> date, not request date (ADR-0014): a refund belongs to the period the
    /// money left, and a refund requested in September but paid in October is October's payment. Reading
    /// only, so an export changes no status and a second export of the same range yields the same rows.
    /// </summary>
    Task<IReadOnlyList<RefundExportRow>> GetRefundExportRowsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
