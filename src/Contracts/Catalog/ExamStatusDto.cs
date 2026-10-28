using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Exams_Status: draft, published, hidden.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExamStatusDto>))]
public enum ExamStatusDto
{
    [JsonStringEnumMemberName("draft")] Draft,
    [JsonStringEnumMemberName("published")] Published,
    [JsonStringEnumMemberName("hidden")] Hidden,
}
