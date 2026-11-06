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
    // for an edit.
    string? CefrLevelCode = null,
    // #230 / D1: dormant, and last so no projection has to name it. Always null on a row an authoring
    // path writes, because nothing reads or sets it; #231 deletes it and this line together.
    int? DifficultyLevel = null);
