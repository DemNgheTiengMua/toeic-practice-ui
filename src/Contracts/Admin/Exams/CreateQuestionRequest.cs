using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record CreateQuestionRequest(
    int GroupId,
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
    // #152: what a spoken answer is judged against - the words to read aloud, or a description of
    // the picture - and which of the two it is. Both null is a free-speech question.
    string? SampleAnswerOrImageDesc = null,
    ReferenceKindDto? ReferenceKind = null,
    // #229 / D1: the level this question is authored at, required by every authoring path. A code, not
    // an enum: the column is a string and a bad value must be a 400 with a sentence, not a
    // deserialisation failure. Optional so every existing call site still compiles - an absent level is
    // refused by name at the service, not defaulted.
    string? CefrLevelCode = null);
