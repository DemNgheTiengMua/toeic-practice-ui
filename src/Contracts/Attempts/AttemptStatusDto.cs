using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Attempts_Status: in_progress, submitted, graded, abandoned.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AttemptStatusDto>))]
public enum AttemptStatusDto
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("submitted")] Submitted,
    [JsonStringEnumMemberName("graded")] Graded,
    [JsonStringEnumMemberName("abandoned")] Abandoned,
}
