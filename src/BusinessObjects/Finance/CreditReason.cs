
namespace BusinessObjects;

/// <summary>CK_CreditTxn_Reason. #246 removed <c>PremiumPath</c> along with the learner path it charged
/// for: the rebuild drops that table, so a ledger row pointing at it has nothing left to point at.
/// #280 added <c>RefundCredit</c>: the credits a credit refund granted, distinct from a bounded
/// <c>AdminGrant</c> make-good.</summary>
public enum CreditReason
{
    Purchase,
    AdminGrant,
    MockExamStart,
    AiGrading,
    MentorBooking,
    AdminRevoke,
    BookingCancel,
    RefundCredit,
}
