using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateQuestionRequest(
    int? GroupId,
    string Stem,
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
    string? RequiredKeywords = null,
    string? SampleAnswerOrImageDesc = null,
    ReferenceKindDto? ReferenceKind = null,
    // #229 / D1: the level this question is authored at, required by the edit path. Read the same way
    // the create path reads it, so a stored level cannot be dropped by an edit that omits it: an edit
    // that states no level keeps the item's own (moving an item between cells is a different act).
    string? CefrLevelCode = null,
    // #230 / D1: dormant, exactly as on <see cref="CreateQuestionRequest"/>. #231 deletes it.
    int? DifficultyLevel = null);
