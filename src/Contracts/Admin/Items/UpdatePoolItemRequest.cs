using System;

namespace Contracts;

/// <summary>
/// #188: an edit to one version's content. Every field here except <see cref="Explanation"/> is
/// scored, so changing any of them inserts a new version rather than rewriting the one a past attempt
/// may have pinned (CNT-43). The item's own coordinates - topic, level, type - are not here: they
/// were decided when the item was authored and moving an item between cells is a different act.
/// </summary>
public sealed record UpdatePoolItemRequest(
    string Stem,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? KeyLabel = null,
    string? Explanation = null,
    int? EvidenceStart = null,
    int? EvidenceEnd = null,
    string? EvidenceUnit = null,
    string? EvidenceQuote = null,
    string? ReferenceText = null,
    ReferenceKindDto? ReferenceKind = null,
    // #194: a per-option rationale. Scored like the option text, because it is rendered beside the
    // option a past attempt chose - a versioned change, not an in-place one.
    string? RationaleA = null,
    string? RationaleB = null,
    string? RationaleC = null,
    string? RationaleD = null,
    string? TargetBands = null);