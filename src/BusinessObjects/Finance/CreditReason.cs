
namespace BusinessObjects;

/// <summary>CK_CreditTxn_Reason. #246 removed <c>PremiumPath</c> along with the learner path it charged
/// for: the rebuild drops that table, so a ledger row pointing at it has nothing left to point at.</summary>
public enum CreditReason
{
    Purchase,
    AdminGrant,
    MockExamStart,
    AiGrading,
    MentorBooking,
    AdminRevoke,
    BookingCancel,
}
