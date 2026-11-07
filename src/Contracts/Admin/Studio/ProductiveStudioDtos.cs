using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #204 / S-07: one productive task and its parts, sent as one document.
/// <para>
/// <b>Why a bundle and not a sequence of items.</b> A two-part writing task is one task with two
/// parts, each carrying its own word range and time budget (D0.7 weights Task 2 twice Task 1). A
/// request shape that carried one item per save would let the second part be forgotten, and the
/// sitting would then score a task whose second half was never authored.
/// </para>
/// <para>
/// <b>Topic and level are not optional.</b> They come from the studio's context - the cell the task
/// joins - and a save without them is refused rather than defaulted, because an item with no cell is
/// stock no cell can report.
/// </para>
/// </summary>
public sealed record ProductiveTaskDraftRequest(
    ModalityDto Modality,
    string Level,
    IReadOnlyList<ProductiveSubTaskInput> SubTasks,
    /// <summary>
    /// The topic code the task's items join. One code for the task: D0.4 gives each type its topic
    /// (W-CORR, W-DATA, W-ARG, W-DISC), and a task whose parts sat on different topics would report
    /// mastery against two cells for one sitting.
    /// </summary>
    string TopicCode,
    /// <summary>D0.4's "prompt 60-100 words": the instruction shown above the parts.</summary>
    string? PromptCopy = null,
    /// <summary>The visual stimulus a synthesis type reads, with the alt text UI-5 requires.</summary>
    string? ImagePath = null,
    string? ImageAlt = null,
    string DomainContext = "general",
    /// <summary>D0.2 / D0.7: whether placement may draw this task, which is what caps its windows.</summary>
    bool PlacementEligible = false);

/// <summary>
/// #204: one part of a productive task. A speaking task carries exactly one; a writing task carries
/// one or the two a two-task sitting is weighted for.
/// </summary>
public sealed record ProductiveSubTaskInput(
    string ItemType,
    string Prompt,
    int? MinWords = null,
    int? MaxWords = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    /// <summary>
    /// What the learner is told after answering. Gate one refuses a productive task without one as
    /// firmly as an objective item (CNT-35): "you were wrong" teaches nothing.
    /// </summary>
    string? Explanation = null);

/// <summary>
/// #204: everything the studio shows before a save, produced by the same review the save runs. A
/// screen that computed its own cap would be a second answer to one question, and the copy that
/// drifted would be the one the author trusted.
/// <para>
/// <b><see cref="PlacementRecordingCap"/> is on the analysis, not only in the refusal.</b> The ticket's
/// point is that a cap applied only at save is a refusal the author meets after doing the work, so the
/// screen renders the cap beside the recording field from the moment a placement task is selected.
/// </para>
/// </summary>
public sealed record ProductiveTaskAnalysisDto(
    IReadOnlyList<ProductiveRefusalDto> Refusals,
    IReadOnlyList<ProductiveRefusalDto> UnrunChecks,
    /// <summary>D0.2's cap in seconds, or null when the task is not placement-eligible.</summary>
    int? PlacementRecordingCap,
    /// <summary>D0.7's scoped word range per part, or null when the part is not placement-scoped.</summary>
    IReadOnlyList<ProductiveWordRangeDto> PlacementWordRanges,
    /// <summary>The band's own practice word range, so the screen can show both figures.</summary>
    IReadOnlyList<ProductiveWordRangeDto> PracticeWordRanges)
{
    public bool Acceptable => Refusals.Count == 0;
}

/// <summary>#204: one part's permitted word range, with the reason it is what it is.</summary>
public sealed record ProductiveWordRangeDto(int Part, int Floor, int Ceiling, bool PlacementScoped);

/// <summary>#204: one rule broken, with the studio rule that produced it.</summary>
public sealed record ProductiveRefusalDto(string Rule, string Gate, string Message);

/// <summary>#204: the studio's pickers. Every list comes from the server, because the topic
/// vocabulary is a closed set of twenty and the bounds are D0.4's - a page holding a second copy of
/// either would drift.</summary>
public sealed record ProductiveStudioOptionsDto(
    IReadOnlyList<ProductiveStudioTopicDto> Topics,
    IReadOnlyList<string> Levels,
    IReadOnlyList<ProductiveTypeOptionDto> Types);

/// <summary>#204: a topic a productive task may sit on, in the closed twenty of that skill.</summary>
public sealed record ProductiveStudioTopicDto(int TopicId, string Code, string Name);

/// <summary>
/// #204: one of D0.4's types with the bounds its parts carry, so the screen can paint the window the
/// author must stay inside without holding its own copy of the table.
/// </summary>
public sealed record ProductiveTypeOptionDto(
    string Code,
    /// <summary>none, optional or required - whether the type reads a visual stimulus.</summary>
    string Visual,
    int? MinimumPrepSeconds,
    int? MaximumPrepSeconds,
    int? MinimumResponseSeconds,
    int? MaximumResponseSeconds,
    /// <summary>D0.4's own word range for the type, before the band and placement narrow it.</summary>
    int? MinimumWords,
    int? MaximumWords);

/// <summary>#204: the task as saved. The parts carry their ids and the draft state they landed in.</summary>
public sealed record ProductiveTaskBundleDto(
    int StimulusId,
    string Level,
    IReadOnlyList<PoolItemDto> Parts);
