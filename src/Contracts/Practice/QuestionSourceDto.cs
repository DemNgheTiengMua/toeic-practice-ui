using System.Text.Json.Serialization;

namespace Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<QuestionSourceDto>))]
public enum QuestionSourceDto
{
    [JsonStringEnumMemberName("all")] All,
    [JsonStringEnumMemberName("wrong_before")] WrongBefore,
    [JsonStringEnumMemberName("unseen")] Unseen,
}
