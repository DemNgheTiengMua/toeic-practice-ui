using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_CreditTxn_Reason: purchase, admin_grant, mock_exam_start, ai_grading, mentor_booking, premium_path, admin_revoke, booking_cancel.</summary>
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
