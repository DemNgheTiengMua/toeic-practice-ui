using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #224: one saved link on the wire. <see cref="SavedQuestionId"/> is the row's own address; a pool
/// link also carries <see cref="ItemId"/> and the pinned <see cref="ItemVersionId"/>, and
/// <see cref="IsRetiredDefective"/> says the item was withdrawn as defective rather than rendering a
/// wrong key as normal (D21).
/// </summary>
public sealed record SavedQuestionItemDto(
    int SavedQuestionId,
    int? QuestionId,
    int? ItemId,
    int? ItemVersionId,
    bool IsRetiredDefective,
    SavedQuestionSourceDto Source,
    DateTimeOffset SavedAt,
    int? SectionId,
    string? SectionCode,
    string? SectionName,
    string? TopicCode,
    string? TopicName,
    int? CategoryId,
    string? CategoryName,
    string Stem,
    string? Passage,
    string? ImagePath,
    string? AudioPath,
    string? Hint,
    string? Explanation,
    string? Transcript,
    IReadOnlyList<SavedQuestionOptionDto> Options)
{
    /// <summary>A section drill's section, or a pool item's topic — never a made-up fallback.</summary>
    public string ScopeCode => SectionCode ?? TopicCode ?? string.Empty;

    public string ScopeName => SectionName ?? TopicName ?? string.Empty;
}
