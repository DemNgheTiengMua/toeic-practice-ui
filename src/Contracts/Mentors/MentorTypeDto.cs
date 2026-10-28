using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Mentor_Type: human, ai.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MentorTypeDto>))]
public enum MentorTypeDto
{
    [JsonStringEnumMemberName("human")] Human,
    [JsonStringEnumMemberName("ai")] Ai,
}
