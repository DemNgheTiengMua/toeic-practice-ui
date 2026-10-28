using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<FeedbackModeDto>))]
public enum FeedbackModeDto
{
    [JsonStringEnumMemberName("immediate")] Immediate,
    [JsonStringEnumMemberName("end")] End,
}
