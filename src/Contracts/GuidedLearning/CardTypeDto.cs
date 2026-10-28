using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_LessonCards_CardType: rule, pitfall, example, micro_check.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardTypeDto>))]
public enum CardTypeDto
{
    [JsonStringEnumMemberName("rule")] Rule,
    [JsonStringEnumMemberName("pitfall")] Pitfall,
    [JsonStringEnumMemberName("example")] Example,
    [JsonStringEnumMemberName("micro_check")] MicroCheck,
}
