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
    // #229 / D1: nullable, because a parser that cannot read a level must say so rather than invent
    // one. It was an `int` filled with 3 at three call sites - the silent default this ticket removes.
    // Dormant: nothing writes it, and #231 deletes it.
    int? DifficultyLevel,
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
    string? EvidenceQuote = null);
