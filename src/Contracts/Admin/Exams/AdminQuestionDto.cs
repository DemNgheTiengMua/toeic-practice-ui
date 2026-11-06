using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminQuestionDto(
    int QuestionId,
    int GroupId,
    int ExamId,
    int SectionId,
    string SectionCode,
    string SectionName,
    int SkillId,
    string SkillCode,
    string Stem,
    // #229 / D1: dormant; the level is what this DTO reports. Null on every row an authoring path
    // writes today, because nothing sets it.
    int? DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation,
    int? CategoryId,
    string? CategoryName,
    int? PrepSeconds,
    int? ResponseSeconds,
    int? MinWords,
    int? MaxWords,
    string? Hint,
    int DisplayOrder,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AdminQuestionOptionDto> Options,
    bool HasAnswers,
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    string? SampleAnswerOrImageDesc = null,
    ReferenceKindDto? ReferenceKind = null,
    // #229 / D1: the level this question is authored at. Written by the authoring paths and read back
    // for an edit; the difficulty beside it is dormant until #231 deletes it.
    string? CefrLevelCode = null);
