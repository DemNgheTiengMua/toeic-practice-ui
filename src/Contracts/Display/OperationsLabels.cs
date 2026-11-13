using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #220 / D19: the staff and finance vocabulary - orders, payments, credits, imports, complaints,
/// mentors. These words are read on an operations screen, so they name the mechanism: <c>status_query</c>
/// is "Status check", not "Status query" left as a code, and <c>return_redirect</c> is "Learner returned
/// from the gateway".
/// </summary>
internal static class OperationsLabels
{
    public static void AddTo(IDictionary<Type, LabelFamily> families)
    {
        LabelFamily.Add<ImportBatchStatusDto>(families, "Import status not reported", [
            ("pending", "Pending"),
            ("done", "Done"),
            ("failed", "Failed"),
        ]);

        LabelFamily.Add<ImportLogLevelDto>(families, "Log level not reported", [
            ("info", "Info"),
            ("warn", "Warning"),
            ("error", "Error"),
        ]);

        LabelFamily.Add<ComplaintStatusDto>(families, "Complaint status not reported", [
            ("open", "Open"),
            ("reviewing", "Under review"),
            ("resolved", "Resolved"),
        ]);

        LabelFamily.Add<ComplaintTypeDto>(families, "Complaint type not reported", [
            ("mentor_no_show", "Mentor did not attend"),
            ("mentor_quality", "Mentor quality"),
            ("grading_issue", "Grading issue"),
            ("technical_error", "Technical error"),
            ("payment_issue", "Payment issue"),
        ]);

        LabelFamily.Add<OrderStatusDto>(families, "Order status not reported", [
            ("pending", "Pending"),
            ("reconciling", "Confirming with the gateway"),
            ("paid", "Paid"),
            ("failed", "Failed"),
            ("expired", "Expired"),
        ]);

        LabelFamily.Add<GatewayStatusDto>(families, "Gateway status not reported", [
            ("pending", "Pending"),
            ("paid", "Paid"),
            ("failed", "Failed"),
            ("expired", "Expired"),
            ("cancelled", "Cancelled"),
        ]);

        LabelFamily.Add<PaymentEventSourceDto>(families, "Event source not reported", [
            ("webhook", "Gateway webhook"),
            ("status_query", "Status check"),
            ("return_redirect", "Learner returned from the gateway"),
            ("cancel", "Cancelled at the gateway"),
        ]);

        LabelFamily.Add<PaymentGatewayDto>(families, "Gateway not reported", [
            ("payos", "payOS"),
            ("mock", "Mock gateway"),
        ]);

        LabelFamily.Add<CreditReasonDto>(families, "Reason not reported", [
            ("purchase", "Credit purchase"),
            ("admin_grant", "Granted by staff"),
            ("mock_exam_start", "Mock exam"),
            ("ai_grading", "AI grading"),
            ("mentor_booking", "Mentor booking"),
            ("admin_revoke", "Revoked by staff"),
            ("booking_cancel", "Booking cancelled"),
            // #281: a refund is a return of value, not the bounded apology an admin_grant is.
            ("refund_credit", "Refund credits"),
            ("placement_retake", "Placement retake"),
        ]);

        LabelFamily.Add<BookingStatusDto>(families, "Booking status not reported", [
            ("pending", "Pending"),
            ("confirmed", "Confirmed"),
            ("done", "Completed"),
            ("cancelled", "Cancelled"),
        ]);

        LabelFamily.Add<SlotStatusDto>(families, "Slot status not reported", [
            ("open", "Open"),
            ("booked", "Booked"),
            ("closed", "Closed"),
        ]);

        LabelFamily.Add<MentorTypeDto>(families, "Mentor type not reported", [
            ("human", "Human mentor"),
            ("ai", "AI coach"),
        ]);

        // #281: the refund vocabulary. A refused refund reads as "Rejected" rather than a status code,
        // and a money refund says so, because it is not the credit path this screen creates.
        LabelFamily.Add<RefundStatusDto>(families, "Refund status not reported", [
            ("requested", "Requested"),
            ("approved", "Approved"),
            ("settled", "Settled"),
            ("rejected", "Rejected"),
        ]);

        LabelFamily.Add<RefundMethodDto>(families, "Refund method not reported", [
            ("money", "Money"),
            ("credit", "Credits"),
        ]);
    }
}
