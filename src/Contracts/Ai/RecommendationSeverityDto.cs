using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<RecommendationSeverityDto>))]
public enum RecommendationSeverityDto
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("minor")] Minor,
    [JsonStringEnumMemberName("major")] Major,
}
