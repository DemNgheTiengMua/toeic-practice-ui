using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>#289: wire mirror of <c>PlacementScope</c> (CK_Placement_Scope).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlacementScopeDto>))]
public enum PlacementScopeDto
{
    [JsonStringEnumMemberName("all_skills")] AllSkills,
    [JsonStringEnumMemberName("listening_and_reading")] ListeningAndReading,
}
