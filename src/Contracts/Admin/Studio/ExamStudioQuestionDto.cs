using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamStudioQuestionDto(
    int QuestionId,
    int GroupId,
    int ExamId,
    int SectionId,
    string SectionCode,
    string SectionName,
    int SkillId,
    string SkillCode,
    string Stem,
    // #229 / D1: dormant. The studio reads and writes <see cref="CefrLevelCode"/>; this stays on the
    // wire so the editor's existing payloads keep deserialising, and #231 deletes it.
    int? DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation,
    int? CategoryId,
    string? CategoryCode,
    string? CategoryName,
    int? PrepSeconds,
    int? ResponseSeconds,
    int? MinWords,
    int? MaxWords,
    string? Hint,
    int DisplayOrder,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<ExamStudioQuestionOptionDto> Options,
    IReadOnlyList<ExamStudioCriterionDto> Criteria,
    bool HasAnswers,
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    string? SampleAnswerOrImageDesc = null,
    ReferenceKindDto? ReferenceKind = null,
    // #229 / D1: the level this question is authored at. The authoring paths write it and an edit
    // reads it back; the difficulty is dormant until #231 deletes it.
    string? CefrLevelCode = null);
