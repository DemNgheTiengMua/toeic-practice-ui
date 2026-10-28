
namespace BusinessObjects;

/// <summary>CK_PaymentEvents_ReportedStatus: pending, paid, failed, expired, cancelled.</summary>
public enum GatewayStatus
{
    Pending,
    Paid,
    Failed,
    Expired,
    Cancelled,
}
