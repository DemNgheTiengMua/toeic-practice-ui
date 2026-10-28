using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Slots_Status: open, booked, closed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SlotStatusDto>))]
public enum SlotStatusDto
{
    [JsonStringEnumMemberName("open")] Open,
    [JsonStringEnumMemberName("booked")] Booked,
    [JsonStringEnumMemberName("closed")] Closed,
}
