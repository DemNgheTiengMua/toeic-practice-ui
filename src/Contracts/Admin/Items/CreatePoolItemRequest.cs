using System;

namespace Contracts;

/// <summary>
/// #188: authoring writes the pool. The request names the item's three coordinates and the version's
/// scored content, and names no exam: an item is not authored "for a paper", so there is no place
/// here to put one. <see cref="ContentTier"/> is deliberately absent - a new item is demo until the
/// review gate promotes it, and a request field would let an author mint production stock.
/// </summary>
public sealed record CreatePoolItemRequest(
    ModalityDto Modality,
    int TopicId,
    string CefrLevelCode,
    string ItemType,
    string Stem,
    // CNT-06: four options and one key, or none at all for a productive task. ItemVersions stores
    // exactly these four columns, so the request mirrors them rather than inventing an option list
    // whose per-option rationale has nowhere to live.
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? KeyLabel = null,
    string? Explanation = null,
    bool PlacementEligible = false,
    int? EvidenceStart = null,
    int? EvidenceEnd = null,
    string? EvidenceUnit = null,
    string? EvidenceQuote = null,
    string? ReferenceText = null,
    ReferenceKindDto? ReferenceKind = null,
    // "A stimulus and its children in one place": the passage or audio this item sits under is
    // created in the same call rather than in a second one nobody remembers to make.
    CreatePoolStimulusRequest? Stimulus = null,
    // The other half of that sentence: a later child joins the stimulus the first one created, so a
    // 2-4-child passage is one stimulus and several children rather than several copies of the text.
    int? StimulusId = null);

/// <summary>#188: the shared passage or audio a set of items hangs off. Pool-owned, no cert.</summary>
public sealed record CreatePoolStimulusRequest(
    string CefrLevelCode,
    string? Passage = null,
    string? AudioPath = null,
    string? Transcript = null,
    string DomainContext = "general",
    string? PromptCopy = null,
    string? ImagePath = null,
    string? ImageAlt = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null);
