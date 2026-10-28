using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Complaints_Type: mentor_no_show, mentor_quality, grading_issue, technical_error, payment_issue.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplaintTypeDto>))]
public enum ComplaintTypeDto
{
    [JsonStringEnumMemberName("mentor_no_show")] MentorNoShow,
    [JsonStringEnumMemberName("mentor_quality")] MentorQuality,
    [JsonStringEnumMemberName("grading_issue")] GradingIssue,
    [JsonStringEnumMemberName("technical_error")] TechnicalError,
    [JsonStringEnumMemberName("payment_issue")] PaymentIssue,
}
