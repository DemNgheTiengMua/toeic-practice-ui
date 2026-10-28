using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SavedQuestionSourceDto>))]
public enum SavedQuestionSourceDto
{
    [JsonStringEnumMemberName("manual")] Manual,
    [JsonStringEnumMemberName("incorrect")] Incorrect,
}
