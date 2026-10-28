using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateQuestionRequest(
    int GroupId,
    string Stem,
    int DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation = null,
    int? CategoryId = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int? MinWords = null,
    int? MaxWords = null,
    string? Hint = null,
    int DisplayOrder = 0,
    IReadOnlyList<AdminQuestionOptionInput>? Options = null,
    IReadOnlyList<int>? CriterionIds = null,
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null);
