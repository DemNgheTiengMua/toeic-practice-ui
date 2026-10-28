using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestParsedQuestionDto(
    int Index,
    int StartLineNumber,
    int EndLineNumber,
    string Stem,
    int DifficultyLevel,
    string? Explanation,
    string? CategoryCode,
    IReadOnlyList<QuickIngestParsedOptionDto> Options,
    IReadOnlyList<QuickIngestIssueDto> Issues,
    bool IsValid,
    string QuestionType = "mcq",
    string? Passage = null,
    string? Transcript = null,
    int? MaxWords = null,
    int? MaxSelections = null,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    string? Hint = null,
    int? GroupNumber = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int? MinWords = null,
    bool AllowNumber = true);
