
namespace BusinessObjects;

/// <summary>CK_Orders_Status: pending, reconciling, paid, failed, expired.</summary>
public enum OrderStatus
{
    Pending,
    Reconciling,
    Paid,
    Failed,
    Expired,
}
