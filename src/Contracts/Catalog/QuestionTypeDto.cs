using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Questions_Type: mcq, mcq_3, multi_select, inline_completion, dropdown, drag_drop, free_response.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuestionTypeDto>))]
public enum QuestionTypeDto
{
    [JsonStringEnumMemberName("mcq")] Mcq,
    [JsonStringEnumMemberName("mcq_3")] Mcq_3,
    [JsonStringEnumMemberName("multi_select")] MultiSelect,
    [JsonStringEnumMemberName("inline_completion")] InlineCompletion,
    [JsonStringEnumMemberName("dropdown")] Dropdown,
    [JsonStringEnumMemberName("drag_drop")] DragDrop,
    [JsonStringEnumMemberName("free_response")] FreeResponse,
}
