
namespace BusinessObjects;

/// <summary>CK_CreditTxn_Reason. <see cref="PremiumPath"/> is retained after #162 deleted the feature:
/// the ledger is append-only and rows already carry it (see STATE.md D27).</summary>
public enum CreditReason
{
    Purchase,
    AdminGrant,
    MockExamStart,
    AiGrading,
    MentorBooking,
    PremiumPath,
    AdminRevoke,
    BookingCancel,
}
