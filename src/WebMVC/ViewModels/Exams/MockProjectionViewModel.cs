using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #222: the projected-level placard on the mock result page, prepared from the API's
/// <see cref="MockResultProjectionDto"/> so the view prints strings rather than deciding meaning.
/// <para>
/// <b>Everything here is labelled.</b> <see cref="CertificateScoreLabel"/> says "certificate score",
/// never "level"; the projected block is titled "projected level" and says it comes from this one paper;
/// and <see cref="SyncOffered"/> only ever comes from the API's own comparison, so the page cannot offer
/// a sync the service would refuse.
/// </para>
/// </summary>
public sealed record MockProjectionViewModel
{
    /// <summary>False when the API could not answer; the page then shows no projected-level card at all.</summary>
    public bool Available { get; init; }

    /// <summary>True for a preserved attempt that predates every cut-score version.</summary>
    public bool Historical { get; init; }

    /// <summary>One row per skill, with its level label or the reason there is none.</summary>
    public IReadOnlyList<MockSkillProjectionViewModel> Skills { get; init; } = [];

    /// <summary>"Reading · B1", naming the skill behind the weakest projection.</summary>
    public string? WeakestProjectedText { get; init; }

    /// <summary>The sentence shown when the table could not place the paper's scores.</summary>
    public string? CannotJudgeText { get; init; }

    /// <summary>What the learner's stored state says now, or that nothing is measured yet.</summary>
    public string StoredStateText { get; init; } = string.Empty;

    /// <summary>True only when a syncable skill differs from the stored state.</summary>
    public bool SyncOffered { get; init; }

    /// <summary>Which skills the sync would write, in words.</summary>
    public string SyncSummaryText { get; init; } = string.Empty;

    /// <summary>The note under the placard stating what a projection is and is not.</summary>
    public string ScopeNote { get; init; } = string.Empty;
}

/// <summary>
/// One skill's line on the placard: its section score and the projected level, or the outcome that
/// refused one. <see cref="LevelLabel"/> is null exactly when the certificate awarded no level, which
/// is the case a non-nullable string would have rendered as "Below A1".
/// </summary>
public sealed record MockSkillProjectionViewModel(
    string SkillName,
    string ScoreDisplay,
    string? LevelLabel,
    string OutcomeLabel,
    string OutcomeClass);
