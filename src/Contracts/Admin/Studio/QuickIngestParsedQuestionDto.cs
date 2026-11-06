using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #189: one parsed question. The pool fields are what the commit needs in order to write an item:
/// a level, a topic and one of D0.4's fourteen types. They are nullable because the parse is also
/// used for a paper that only ever reaches the legacy shape, and because a parser can read a level
/// only when the pasted text states one.
/// </summary>
public sealed record QuickIngestParsedQuestionDto(
    int Index,
    int StartLineNumber,
    int EndLineNumber,
    string Stem,
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
    bool AllowNumber = true,
    string? SampleAnswerOrImageDesc = null,
    ReferenceKindDto? ReferenceKind = null,
    /// <summary>#229: the authored level. Required for the pool write and never defaulted.</summary>
    string? CefrLevel = null,
    string? TopicCode = null,
    string? ItemType = null,
    bool PlacementEligible = false,
    int? EvidenceStart = null,
    int? EvidenceEnd = null,
    string? EvidenceUnit = null,
    string? EvidenceQuote = null,
    // #230 / D1: dormant, and last so no parser call site has to name it. A parser that cannot read a
    // level says so rather than inventing one, and never fills this in as a substitute. #231 deletes it.
    int? DifficultyLevel = null);
