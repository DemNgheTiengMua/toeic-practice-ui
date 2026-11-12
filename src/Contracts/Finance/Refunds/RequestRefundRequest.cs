namespace Contracts;

/// <summary>
/// A staff member's refund request. The method is on the body rather than implied by the route so the
/// (#281) credit path and the (#282) money path reach one service, and the refusal for a shape the
/// method does not allow happens there rather than in a screen.
/// <para>
/// <b>The method owns the value column.</b> A credit refund sends <paramref name="CreditCount"/>; a
/// money refund sends <paramref name="Amount"/>. Sending the other column is refused rather than
/// ignored, because a silently dropped figure is a refund that records something nobody typed.
/// </para>
/// </summary>
public sealed record RequestRefundRequest(
    int UserId,
    RefundMethodDto Method,
    int CreditCount,
    string Reason,
    int? OrderId = null,
    int? ComplaintId = null,
    decimal? Amount = null);
