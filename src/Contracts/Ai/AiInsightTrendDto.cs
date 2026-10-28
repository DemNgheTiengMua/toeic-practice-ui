using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AiInsightTrendDto>))]
public enum AiInsightTrendDto
{
    [JsonStringEnumMemberName("improving")] Improving,
    [JsonStringEnumMemberName("flat")] Flat,
    [JsonStringEnumMemberName("declining")] Declining,
}
