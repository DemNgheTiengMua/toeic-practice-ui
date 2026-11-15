using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamStudioQuestionDto(
    int QuestionId,
    /// <summary>
    /// #362: the pool item this position pins. <see cref="QuestionId"/> is a paper-position handle
    /// (the roster id), which is what an answer row names - so an editor that wants to change the
    /// content must send THIS to <c>PUT /items/{itemId}</c>, not the position handle. Without it the
    /// screen can render a question and cannot edit one.
    /// </summary>
    int ItemId,
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
    // reads it back.
    string? CefrLevelCode = null,
    // #362: the pool topic this item sits in. Adding a child to an existing Bundle has to author it in
    // the same cell as its siblings, and CreatePoolItemRequest requires a topic it cannot guess.
    int TopicId = 0);
