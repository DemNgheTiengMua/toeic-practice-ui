using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Placement_Status: in_progress, done, abandoned.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlacementStatusDto>))]
public enum PlacementStatusDto
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("abandoned")] Abandoned,
}
