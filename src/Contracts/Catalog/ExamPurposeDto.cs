using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Exams_Purpose: mock, placement, speaking_practice, writing_practice.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExamPurposeDto>))]
public enum ExamPurposeDto
{
    [JsonStringEnumMemberName("mock")] Mock,
    [JsonStringEnumMemberName("placement")] Placement,
    [JsonStringEnumMemberName("speaking_practice")] SpeakingPractice,
    [JsonStringEnumMemberName("writing_practice")] WritingPractice,
}
