using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_CreditTxn_Reason. <see cref="PremiumPath"/> stays after #162 removed the feature: past ledger
/// rows carry it, and this enum mirrors the CHECK constraint (STATE.md D27).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CreditReasonDto>))]
public enum CreditReasonDto
{
    [JsonStringEnumMemberName("purchase")] Purchase,
    [JsonStringEnumMemberName("admin_grant")] AdminGrant,
    [JsonStringEnumMemberName("mock_exam_start")] MockExamStart,
    [JsonStringEnumMemberName("ai_grading")] AiGrading,
    [JsonStringEnumMemberName("mentor_booking")] MentorBooking,
    [JsonStringEnumMemberName("premium_path")] PremiumPath,
    [JsonStringEnumMemberName("admin_revoke")] AdminRevoke,
    [JsonStringEnumMemberName("booking_cancel")] BookingCancel,
}
