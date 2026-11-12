
namespace BusinessObjects;

/// <summary>CK_Refunds_Status. A credit refund terminates at <see cref="Approved"/>: the ledger,
/// not a bank, is where the value lands, and <c>CK_Refunds_SettledIsMoney</c> refuses otherwise.</summary>
public enum RefundStatus
{
    Requested,
    Approved,
    Settled,
    Rejected,
}
