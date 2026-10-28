using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Steps_Status: locked, available, in_progress, done.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearnerPathStepStatusDto>))]
public enum LearnerPathStepStatusDto
{
    [JsonStringEnumMemberName("locked")] Locked,
    [JsonStringEnumMemberName("available")] Available,
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("done")] Done,
}
