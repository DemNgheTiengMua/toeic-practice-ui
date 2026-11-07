using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #203 / S-05: one stimulus and the children authored with it, sent as one document.
/// <para>
/// <b>Why the studio posts a bundle and not a sequence of items.</b> Gate four's span-independence
/// rule and CNT-10's order and gap are statements about a <em>set</em>: four items judged one at a
/// time can each be independent and still be a defective bundle. A request shape that carried one item
/// would have no way to express the rule, so the seam would be asked to hold a fact it cannot see.
/// </para>
/// <para>
/// <see cref="Children"/> is in the order the items will be served. For a listening bundle that order
/// is CNT-10's requirement, not a display preference, and the service refuses a bundle whose authored
/// order disagrees with its evidence.
/// </para>
/// </summary>
public sealed record StimulusDraftRequest(
    ModalityDto Modality,
    string StimulusLevel,
    string ItemType,
    IReadOnlyList<StimulusDraftChild> Children,
    string? Passage = null,
    string? Transcript = null,
    string? AudioPath = null,
    string DomainContext = "general");

/// <summary>
/// #203: one child as the studio holds it before a save. <see cref="Position"/> is 1-based and must
/// match the list's own order; <see cref="TopicCode"/> is the child's own topic, because siblings on
/// one passage deliberately test different topics. The code rather than the id, because the closed
/// twenty are addressed by code everywhere else and a stale id picked from a cached page would point
/// at the wrong cell.
/// </summary>
public sealed record StimulusDraftChild(
    int Position,
    string CefrLevel,
    string TopicCode,
    string Stem,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? KeyLabel = null,
    string? Explanation = null,
    string? RationaleA = null,
    string? RationaleB = null,
    string? RationaleC = null,
    string? RationaleD = null,
    int? EvidenceStart = null,
    int? EvidenceEnd = null,
    string? EvidenceQuote = null,
    bool PlacementEligible = false);

/// <summary>
/// #203: everything the studio shows before a save, produced by the same review the save runs. A
/// screen that computed its own overlap or gauges would be a second answer to one question, and the
/// copy that drifted would be the one the author trusted.
/// <para>
/// <b>Refusals and unrun checks are separate lists, and the split is load-bearing.</b> A refusal is
/// something the author must change before the bundle can be written. An unrun check is something the
/// deployment could not do - gate three needs two verifier models, gate two's acoustic rule an
/// independent transcription - and it blocks nothing, because the save writes a <c>draft</c>. Merging
/// the two would make the screen say "cannot save" about a bundle the save accepts, which is the
/// contradiction the first draft of this screen had.
/// </para>
/// </summary>
public sealed record StimulusDraftAnalysisDto(
    IReadOnlyList<StimulusRefusalDto> Refusals,
    IReadOnlyList<StimulusRefusalDto> UnrunChecks,
    IReadOnlyList<LevelGaugeDto> Gauges,
    string LevelVerdict,
    IReadOnlyList<StimulusOverlapDto> Overlaps,
    IReadOnlyList<StimulusGapDto> Gaps,
    /// <summary>The band the measured text fits, or null when it fits none of the six.</summary>
    string? FittingBand = null,
    /// <summary>The band the author selected, so the control can say when the two disagree.</summary>
    string? SelectedBand = null)
{
    /// <summary>A bundle the studio will save: nothing it can judge is outstanding.</summary>
    public bool Acceptable => Refusals.Count == 0;

    /// <summary>The largest pairwise overlap, which is the figure the indicator prints.</summary>
    public decimal MaximumOverlap
    {
        get
        {
            var maximum = 0m;
            foreach (var overlap in Overlaps)
            {
                if (overlap.Overlap > maximum)
                {
                    maximum = overlap.Overlap;
                }
            }

            return maximum;
        }
    }
}

/// <summary>
/// #203: one refusal, with the gate that produced it. <see cref="Gate"/> is the human name of the
/// gate whose rule refused the bundle, because "the bundle is invalid" tells an author which field to
/// change but not which rule to satisfy.
/// </summary>
public sealed record StimulusRefusalDto(string Rule, string Gate, string Message);

/// <summary>#203 / ADM-08: one live gauge. <see cref="Inside"/> is null where the band sets no bound.</summary>
public sealed record LevelGaugeDto(string Label, string Value, string Target, bool? Inside);

/// <summary>#203 / CNT-12: the overlap between two children, and whether it is over the ceiling.</summary>
public sealed record StimulusOverlapDto(int FirstPosition, int SecondPosition, decimal Overlap, bool OverCeiling);

/// <summary>#203 / CNT-10: the seconds between two adjacent audio children's evidence.</summary>
public sealed record StimulusGapDto(int AfterPosition, int BeforePosition, decimal Seconds, bool BelowFloor);

/// <summary>#203: a topic a child may sit on, in the closed twenty.</summary>
public sealed record StimulusStudioTopicDto(int TopicId, string Code, string Name);

/// <summary>
/// #203 / ADM-07: what a stimulus of one level and type locks. <see cref="Locked"/> pre-creates the
/// slots; <see cref="AllowedChildLevels"/> is D0.3's envelope; <see cref="MinimumAtLevel"/> is the
/// two-thirds rule. The screen reads these rather than holding its own copy of D0.3.
/// </summary>
public sealed record StimulusStudioQuotaDto(
    string Level,
    string ItemType,
    int Locked,
    int Minimum,
    int Maximum,
    IReadOnlyList<string> AllowedChildLevels,
    int MinimumAtLevel);

/// <summary>
/// #203: the studio's pickers. Every list comes from the server because the topic vocabulary is a
/// closed set of twenty and the quotas are D0.3 - a page holding a second copy of either would drift.
/// </summary>
public sealed record StimulusStudioOptionsDto(
    IReadOnlyList<StimulusStudioTopicDto> Topics,
    IReadOnlyList<string> Levels,
    IReadOnlyList<string> ItemTypes,
    IReadOnlyList<StimulusStudioQuotaDto> Quotas,
    string SpanUnit);

/// <summary>#203: the bundle as saved. The children carry their ids and the draft state they landed in.</summary>
public sealed record StimulusBundleDto(
    int StimulusId,
    string StimulusLevel,
    IReadOnlyList<PoolItemDto> Children);
