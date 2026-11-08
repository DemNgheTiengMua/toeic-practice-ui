using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #216 / PLC-14: the returning learner's screen. It names the five facts the ticket requires and
/// offers exactly two actions - resume at the exact item, or abandon.
/// <para>
/// The words, not the colours, carry the state (§5: the word is the first layer), and every number is
/// printed in <c>IBM Plex Mono</c> through its own class.
/// </para>
/// </summary>
public sealed record PlacementResumePageViewModel(
    PlacementResumeViewDto Resume,
    string? ErrorMessage = null)
{
    public bool HasError { get; } = !string.IsNullOrEmpty(ErrorMessage);

    public string StartedAtDisplay { get; init; } = string.Empty;

    /// <summary>"3 of 24 answers saved" - the server's count, never the browser's queue.</summary>
    public string SyncedSummary =>
        $"{Resume.SyncedAnswerCount} of {Resume.TotalQuestions} answers saved";

    /// <summary>"Stage 2 of 4 - Reading", or a sentence when the sitting names no active stage.</summary>
    public string StageSummary => Resume.ActiveStage is int stage
        ? $"Stage {stage} of {Resume.TotalStages} — {Resume.ActiveSkillName}"
        : "No stage is active yet";

    public IReadOnlyList<PlacementResumeSkillRow> SkillRows { get; init; } = [];
    public string ResumeUrl => $"/placement/take/{Resume.PlacementId}";
    public string AbandonUrl => $"/placement/take/{Resume.PlacementId}/abandon";

    public static PlacementResumePageViewModel From(
        PlacementResumeViewDto resume,
        WebMVC.Formatters.DateTimeFormatter dates,
        string? errorMessage = null) =>
        new(resume, errorMessage)
        {
            StartedAtDisplay = dates.FormatDate(resume.StartedAt),
            SkillRows = resume.Skills.Select(skill => new PlacementResumeSkillRow(
                skill.Code,
                skill.Name,
                // The state is a WORD. A dot of colour would leave the meaning to the palette, and
                // §5 forbids that.
                skill.IsDone ? "Done" : skill.IsActive ? "In progress" : "Not started",
                $"{skill.AnsweredCount} of {skill.TotalQuestions}",
                skill.IsDone ? "resume-skill resume-skill--done"
                    : skill.IsActive ? "resume-skill resume-skill--active" : "resume-skill"))
            .ToList(),
        };
}

public sealed record PlacementResumeSkillRow(
    string Code,
    string Name,
    string StateText,
    string AnsweredSummary,
    string CssClass);
