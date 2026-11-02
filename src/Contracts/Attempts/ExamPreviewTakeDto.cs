using System.Collections.Generic;

namespace Contracts;

/// <summary>#158: the answer key for one question. Only the admin preview endpoint sends this.</summary>
public sealed record ExamPreviewAnswerDto(
    int QuestionId,
    IReadOnlyList<int> CorrectOptionIds,
    string? Explanation);

public sealed record ExamPreviewSectionDto(
    int SectionId,
    string SectionCode,
    string SectionName,
    int FirstDisplayOrder,
    int LastDisplayOrder,
    int QuestionCount);

public sealed record ExamPreviewTakeDto(
    AttemptTakeDto Take,
    IReadOnlyList<ExamPreviewAnswerDto> Answers,
    IReadOnlyList<ExamPreviewSectionDto> Sections,
    int DurationMinutes,
    string ExamStatus);
