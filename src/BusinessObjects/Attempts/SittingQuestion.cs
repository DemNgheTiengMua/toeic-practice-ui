using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #250: one position on a paper, shaped so the sitting projection reads it exactly as it read a
/// <see cref="Entities.Question"/>.
/// <para>
/// <b>Why this type exists.</b> The sitting path read <c>Question</c> rows for two different jobs:
/// <i>what was asked</i> (stem, options, key - which the pool owns through <c>ItemVersions</c>) and
/// <i>where it sits on the paper</i> (section, position, unit, navigation policy - which the paper
/// owns). Joining those into one entity is why the engine could not stop reading <c>Questions</c>: the
/// projection had no way to ask for the second without the first.
/// </para>
/// <para>
/// <b>Why the nested shapes.</b> A first draft flattened section and group onto this record, and every
/// projection expression had to be rewritten - 21 sites, and the rewrite went wrong four times. The
/// nested records below carry the same member names the projection already used (<c>q.Section.Name</c>,
/// <c>q.Section.Skill.Code</c>, <c>q.Group.UnitNumber</c>), so switching the source of a paper does not
/// touch the code that renders it. That is the whole point of a seam.
/// </para>
/// <para>
/// <b><see cref="Id"/> is a paper-position handle, not a foreign key.</b> For a legacy paper it is the
/// question's id and it is what an answer row names. For a pool paper it is the roster position's id.
/// The wire contract keeps calling it <c>questionId</c> and the client keeps using it as the answer
/// key, which is why the answer protocol does not change when the content model does.
/// </para>
/// </summary>
public sealed record SittingQuestion(
    int Id,
    int ExamId,
    /// <summary>The roster row this position is, or NULL for a legacy paper.</summary>
    int? ExamItemId,
    /// <summary>The pinned version, or NULL for a legacy paper.</summary>
    int? ItemVersionId,
    int ItemId,
    int SectionId,
    string RenderFormat,
    string Stem,
    IReadOnlyList<SittingOption> Options,
    int? MinWords,
    int? MaxWords,
    int? PrepSeconds,
    int? ResponseSeconds,
    int? MaxSelections,
    bool AllowNumber,
    string? AcceptedAnswersJson,
    string? RequiredKeywords,
    string? Hint,
    int? GroupId,
    SittingSection Section,
    SittingGroup Group,
    /// <summary>
    /// The legacy question row this position was read from, or NULL for a pool position. Only the
    /// legacy scoring path needs it; nothing else may branch on it.
    /// </summary>
    Entities.Question? LegacyQuestion = null);

/// <summary>The paper's chrome for one position - the section row, without the question.</summary>
public sealed record SittingSection(
    int SectionId,
    string Code,
    string Name,
    int SkillId,
    string Modality,
    string? LayoutType,
    bool RequirePassage,
    string? NavigationMode,
    string? ToolsConfigJson,
    bool HideOptionText,
    int OptionCount,
    int ClusterSize,
    bool RequireImage,
    string? TimerPolicyJson,
    SittingSkill Skill);

/// <summary>The skill a section belongs to. The phase bar and the one-way lock are read from here.</summary>
public sealed record SittingSkill(
    int SkillId,
    string Code,
    string Name,
    int DisplayOrder,
    int? DurationMinutes,
    bool LockNavigationOneWay);

/// <summary>
/// The shared stimulus a position sits in: passage, audio slice, image. A pool position's group IS its
/// stimulus; a legacy position's group is a <c>QuestionGroup</c> row.
/// </summary>
public sealed record SittingGroup(
    int? UnitNumber,
    string? Passage,
    string? Transcript,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    /// <summary>
    /// The group's own response window, used only by the HYBRID_GROUP_LOCKED phase clock. It belongs to
    /// the group because a locked group is one timed block, not a set of independently timed questions.
    /// Null for a pool position, whose window comes from the item version or the stimulus.
    /// </summary>
    int? ResponseSeconds = null);

/// <summary>One option of a <see cref="SittingQuestion"/>.</summary>
/// <param name="OptionId">
/// The id the client echoes back. A pool option has no row to point at (an <c>ItemVersion</c> stores
/// labels A-D), so the engine derives a stable id from the version; see <c>AttemptEngine.PoolOptionId</c>.
/// </param>
public sealed record SittingOption(int OptionId, string Label, string Text);
