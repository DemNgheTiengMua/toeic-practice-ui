using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_CreditTxn_Reason. #246 removed <c>PremiumPath</c>: the learner path it charged for is
/// dropped in the rebuild, and this enum mirrors the CHECK constraint.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CreditReasonDto>))]
public enum CreditReasonDto
{
    [JsonStringEnumMemberName("purchase")] Purchase,
    [JsonStringEnumMemberName("admin_grant")] AdminGrant,
    [JsonStringEnumMemberName("mock_exam_start")] MockExamStart,
    [JsonStringEnumMemberName("ai_grading")] AiGrading,
    [JsonStringEnumMemberName("mentor_booking")] MentorBooking,
    [JsonStringEnumMemberName("admin_revoke")] AdminRevoke,
    [JsonStringEnumMemberName("booking_cancel")] BookingCancel,
}
