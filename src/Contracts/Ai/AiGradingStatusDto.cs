using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_AiGradings_Status: pending, done, failed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiGradingStatusDto>))]
public enum AiGradingStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}
