using System;

namespace Contracts;

/// <summary>
/// #188: an item as authoring sees it - the stable identity, the version this answer describes, and
/// how many versions exist. <see cref="VersionCount"/> is what lets a caller tell "my scored edit
/// forked a version" from "my explanation typo did not", without a second read.
/// </summary>
public sealed record PoolItemDto(
    int ItemId,
    int ItemVersionId,
    int VersionNumber,
    int VersionCount,
    ModalityDto Modality,
    int TopicId,
    string TopicCode,
    string CefrLevelCode,
    string ItemType,
    string ContentTier,
    string LifecycleStatus,
    bool PlacementEligible,
    int? StimulusId,
    string Stem,
    string? OptionA,
    string? OptionB,
    string? OptionC,
    string? OptionD,
    string? KeyLabel,
    string? Explanation,
    int? EvidenceStart,
    int? EvidenceEnd,
    string? EvidenceUnit,
    string? EvidenceQuote,
    string? ReferenceText,
    ReferenceKindDto? ReferenceKind,
    DateTimeOffset CreatedAt,
    // #194: why each option is what it is. Gate one refuses an item without these, so authoring
    // reads them back rather than making the author retype them.
    string? RationaleA = null,
    string? RationaleB = null,
    string? RationaleC = null,
    string? RationaleD = null);
