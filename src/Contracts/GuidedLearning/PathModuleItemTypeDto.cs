using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_PMI_Type: practice_section, mock_exam, ai_task, mentor, lesson.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PathModuleItemTypeDto>))]
public enum PathModuleItemTypeDto
{
    [JsonStringEnumMemberName("practice_section")] PracticeSection,
    [JsonStringEnumMemberName("mock_exam")] MockExam,
    [JsonStringEnumMemberName("ai_task")] AiTask,
    [JsonStringEnumMemberName("mentor")] Mentor,
    [JsonStringEnumMemberName("lesson")] Lesson,
}
