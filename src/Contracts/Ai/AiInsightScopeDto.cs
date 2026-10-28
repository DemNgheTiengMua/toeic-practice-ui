using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AiInsightScopeDto>))]
public enum AiInsightScopeDto
{
    [JsonStringEnumMemberName("practice_session")] PracticeSession,
    [JsonStringEnumMemberName("attempt")] Attempt,
    [JsonStringEnumMemberName("path")] Path,
    [JsonStringEnumMemberName("dashboard")] Dashboard,
}
