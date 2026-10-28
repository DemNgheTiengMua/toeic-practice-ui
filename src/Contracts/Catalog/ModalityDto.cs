using System.Text.Json.Serialization;

namespace Contracts;

// Wire mirrors of the BusinessObjects enums (issue #2: enums for status strings). Each member
// serialises as the database code it stands for, so the JSON on the wire is unchanged
// ("in_progress", not "InProgress"). Named <Domain>Dto like every Contracts type, so files importing both namespaces stay unambiguous.

/// <summary>CK_Skills_Modality: listening, reading, speaking, writing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModalityDto>))]
public enum ModalityDto
{
    [JsonStringEnumMemberName("listening")] Listening,
    [JsonStringEnumMemberName("reading")] Reading,
    [JsonStringEnumMemberName("speaking")] Speaking,
    [JsonStringEnumMemberName("writing")] Writing,
}
