using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #250: one position on a paper, as the sitting projection reads it.
/// <para>
/// <b>Why this type exists.</b> The sitting path read <c>Question</c> rows for two different jobs:
/// <i>what was asked</i> (stem, options, key — which the pool owns through <c>ItemVersions</c>) and
/// <i>where it sits on the paper</i> (section, position, unit, navigation policy — which the paper
/// owns). Joining those two into one entity is why <c>AttemptEngine</c> could not stop reading
/// <c>Questions</c>: the projection had no way to ask for the second without the first.
/// </para>
/// <para>
/// This record is the seam. A legacy paper maps a <see cref="Entities.Question"/> into it; a pool paper
/// maps an <c>ExamItems</c> roster row into it. Everything downstream — navigation locks, clustering,
/// the take view, the navigator — sees only this, so a paper built from the pool and a paper built from
/// the legacy rows are the same shape to every reader after this point.
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
    int SectionId,
    string SectionCode,
    string SectionName,
    int SkillId,
    string SkillCode,
    string SkillName,
    int SkillDisplayOrder,
    int? SkillDurationMinutes,
    string Modality,
    /// <summary>The widget: one of the seven render formats. Legacy papers store it as <c>QuestionType</c>.</summary>
    string RenderFormat,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
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
    string? LayoutType,
    bool RequirePassage,
    string? NavigationMode,
    string? ToolsConfigJson,
    bool HideOptionText,
    int OptionCount,
    int ClusterSize,
    bool RequireImage,
    int UnitNumber,
    /// <summary>The grouping identity the navigation locks compare. A stimulus, in pool terms.</summary>
    int? GroupId,
    string? TimerPolicyJson,
    /// <summary>
    /// The legacy question row this position was read from, or NULL for a pool position. Only the
    /// legacy scoring path needs it; nothing else may branch on it.
    /// </summary>
    Entities.Question? LegacyQuestion = null);

/// <summary>One option of a <see cref="SittingQuestion"/>.</summary>
/// <param name="OptionId">
/// The id the client echoes back. A pool option has no row to point at (an <c>ItemVersion</c> stores
/// labels A–D), so the engine derives a stable id from the version; see <c>AttemptEngine.PoolOptionId</c>.
/// </param>
public sealed record SittingOption(int OptionId, string Label, string Text);
