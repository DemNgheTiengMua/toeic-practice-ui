using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Sections_NavigationMode: FULL_SANDBOX, UNIT_LOCKED_SANDBOX, AUDIO_DRIVEN, SYSTEM_PACED_AUTO, HYBRID_GROUP_LOCKED.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NavigationModeDto>))]
public enum NavigationModeDto
{
    [JsonStringEnumMemberName("FULL_SANDBOX")] FullSandbox,
    [JsonStringEnumMemberName("UNIT_LOCKED_SANDBOX")] UnitLockedSandbox,
    [JsonStringEnumMemberName("AUDIO_DRIVEN")] AudioDriven,
    [JsonStringEnumMemberName("SYSTEM_PACED_AUTO")] SystemPacedAuto,
    [JsonStringEnumMemberName("HYBRID_GROUP_LOCKED")] HybridGroupLocked,
}
