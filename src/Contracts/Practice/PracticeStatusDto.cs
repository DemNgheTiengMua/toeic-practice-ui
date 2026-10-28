using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<PracticeStatusDto>))]
public enum PracticeStatusDto
{
    [JsonStringEnumMemberName("in_progress")] InProgress,
    [JsonStringEnumMemberName("finished")] Finished,
    [JsonStringEnumMemberName("abandoned")] Abandoned,
}
