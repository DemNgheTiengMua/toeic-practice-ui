
namespace Contracts;

/// <summary>
/// A staff member's refund request. The method is on the body rather than implied by the route so a
/// money refund can reach the service and be refused there: a screen that never sends <c>money</c>
/// would make the refusal unreachable, and an unreachable guard is not a guard.
/// </summary>
public sealed record RequestRefundRequest(
    int UserId,
    RefundMethodDto Method,
    int CreditCount,
    string Reason,
    int? OrderId = null,
    int? ComplaintId = null);
