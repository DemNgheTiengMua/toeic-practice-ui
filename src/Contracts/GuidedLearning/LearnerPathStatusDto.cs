using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_LearnerPaths_Status: active, completed, reset.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearnerPathStatusDto>))]
public enum LearnerPathStatusDto
{
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("completed")] Completed,
    [JsonStringEnumMemberName("reset")] Reset,
}
