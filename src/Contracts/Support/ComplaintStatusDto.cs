using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Complaints_Status: open, reviewing, resolved.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplaintStatusDto>))]
public enum ComplaintStatusDto
{
    [JsonStringEnumMemberName("open")] Open,
    [JsonStringEnumMemberName("reviewing")] Reviewing,
    [JsonStringEnumMemberName("resolved")] Resolved,
}
