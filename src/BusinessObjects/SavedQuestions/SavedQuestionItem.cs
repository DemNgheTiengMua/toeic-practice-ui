using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #224: one saved link, rendered from the version it pinned.
/// <para>
/// A pool link carries <see cref="ItemId"/> and <see cref="ItemVersionId"/>; its stem, options, key and
/// explanation are read from that version, never from the item's latest one. A legacy link carries
/// <see cref="QuestionId"/> and renders its question row, unchanged.
/// </para>
/// <para>
/// <see cref="IsRetiredDefective"/> is about the item's lifecycle <b>now</b>, not the version's content:
/// a withdrawn item still renders what the learner saved, flagged, because showing a wrong key silently
/// is the defect this exists to prevent (D21).
/// </para>
/// </summary>
public sealed record SavedQuestionItem(
    int SavedQuestionId,
    int? QuestionId,
    int? ItemId,
    int? ItemVersionId,
    bool IsRetiredDefective,
    SavedQuestionSource Source,
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
    IReadOnlyList<SavedQuestionOption> Options)
{
    /// <summary>
    /// The one label a card shows: a section drill's section, or a pool item's topic. A pool item has
    /// no section at all, so pretending it has one would put a made-up id in the section filter.
    /// </summary>
    public string ScopeCode => SectionCode ?? TopicCode ?? string.Empty;

    public string ScopeName => SectionName ?? TopicName ?? string.Empty;
}
