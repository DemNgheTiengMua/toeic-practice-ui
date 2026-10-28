using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Certificates_ScoreAggregationRule: sum, avg_half_step, avg_whole, independent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScoreAggregationRuleDto>))]
public enum ScoreAggregationRuleDto
{
    [JsonStringEnumMemberName("sum")] Sum,
    [JsonStringEnumMemberName("avg_half_step")] AvgHalfStep,
    [JsonStringEnumMemberName("avg_whole")] AvgWhole,
    [JsonStringEnumMemberName("independent")] Independent,
}
