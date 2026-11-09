using Contracts;
using WebMVC.Formatters;

namespace WebMVC.Models;

/// <summary>#159 / #241: the three-block dashboard, prepared for rendering. Every value here is a
/// presentation decision (a label, a link, a formatted date); no number is recomputed, so the page
/// cannot disagree with the service that measured it.
/// <para>
/// <b>#241 deleted the progress bar rather than restyling it.</b> <see cref="WeekProgressPercent"/>,
/// <see cref="WeekProgressValue"/> and the <c>role="progressbar"</c> markup are gone: D21a says the
/// goal block is four tabular cells and no progress bars, and a bar that "is not technically a bar"
/// (a div with a width) fails the same rule. What replaces it is a sentence with the two numbers in
/// it, which is also what a screen reader reads without any ARIA at all.
/// </para>
/// </summary>
public sealed class DashboardPresentation
{
    /// <summary>LRN-15: the exam date becomes an attention countdown inside this window.</summary>
    public const int CountdownWindowDays = 14;

    public LearnerDashboardDto Dashboard { get; }
    public string CertificateUrl { get; }

    /// <summary>The exam date as a date, or the countdown inside <see cref="CountdownWindowDays"/>.</summary>
    public string TargetDateDisplay { get; }

    /// <summary>True inside the countdown window, which is what turns the date gold.</summary>
    public bool IsCountdown { get; }

    /// <summary>Block 2, cell 1: the learner's current level against the target.</summary>
    public string LevelCell { get; }

    /// <summary>Block 2, cell 2: D21a's remaining guided hours, or the honest "cannot judge".</summary>
    public string RemainingHoursCell { get; }

    /// <summary>Block 2, cell 3: declared pace and the runway it has to cover.</summary>
    public string PacingCell { get; }

    /// <summary>Block 2, cell 4: the STORED verdict, never a recomputation of it.</summary>
    public string VerdictLabel { get; }

    /// <summary>The verdict badge class, a second layer over <see cref="VerdictLabel"/>.</summary>
    public string VerdictBadge { get; }

    /// <summary>Block 3: the certificate's skill count, which is the grid's column count.</summary>
    public int SkillColumnCount { get; }

    /// <summary>Block 3's grid class, carrying the column count so CSS never guesses it.</summary>
    public string SkillsGridClass { get; }

    /// <summary>Block 1: this week's execution, as two numbers in a sentence.</summary>
    public string WeekPaceText { get; }

    /// <summary>The cascade step that fired, worded for the learner (LRN-14: "the cascade is data").</summary>
    public string CascadeLabel { get; }

    public DashboardPresentation(LearnerDashboardDto dashboard, DateTimeFormatter dates, DateOnly? today = null)
    {
        Dashboard = dashboard;
        CertificateUrl = $"/certificates#cert-{dashboard.Certificate.Code.ToLowerInvariant()}";

        var goal = dashboard.Goal;
        var now = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // LRN-15 / U22: a date normally; a countdown only inside the window, because a perpetual
        // "365 days left" is noise and inside two weeks the pressure is real. A date in the past is
        // not a countdown - it is a deadline that has gone, and saying "-3 days" would be worse.
        var daysLeft = goal.TargetDate is { } target ? target.DayNumber - now.DayNumber : (int?)null;
        IsCountdown = daysLeft is >= 0 and <= CountdownWindowDays;
        TargetDateDisplay = goal.TargetDate is { } date
            ? IsCountdown
                ? daysLeft == 0 ? "Exam today" : $"{daysLeft} day{(daysLeft == 1 ? "" : "s")} to exam"
                : date.ToString("d MMM yyyy")
            : "";

        // "B1 to B2" rather than an arrow glyph: §5 forbids raw unicode in the markup, and icons.js
        // decorates by label rather than inside a table cell.
        LevelCell = goal.CurrentBandCode is { } current && goal.TargetBandCode is { } targetBand
            ? $"{current} to {targetBand}"
            : goal.TargetBandCode is { } onlyTarget
                ? $"Not placed to {onlyTarget}"
                : "No target set";

        // #234 / D21: the stored ΔH, or nothing. A certificate whose bands carry no study hours has no
        // number, so this says so rather than prorating one.
        RemainingHoursCell = goal.RequiredTotalHours is { } hours
            ? $"{hours} h"
            : "cannot judge";

        PacingCell = goal.WeeklyStudyHours is { } pace
            ? goal.TargetDate is { } runway
                ? $"{pace:0.#} h/week · {WeeksLeft(now, runway)} wk"
                : $"{pace:0.#} h/week"
            : "No pace set";

        // #241 / #244: the verdict is the STORED one. No target, no deadline, or a certificate whose
        // bands carry no hours means there is nothing to judge - not "unrealistic", and not a
        // confident answer the service never wrote.
        (VerdictLabel, VerdictBadge) = goal.Status switch
        {
            "feasible" => ("On pace for the date", "badge--success"),
            "stretch" => ("Achievable with more hours a week", "badge--warning"),
            "unrealistic" => ("Not achievable before the date", "badge--danger"),
            _ => (goal.TargetBandCode is null ? "Cannot judge: no target set" : "Cannot judge", "badge--muted"),
        };

        // Block 3's column count IS the certificate's skill count (#241's explicit criterion). Two
        // skills is two columns, never a four-column grid with two empty cells, so the class carries
        // the number and CSS has one rule per count rather than a fixed template.
        SkillColumnCount = dashboard.Skills.Count;
        SkillsGridClass = $"dashboard-skills__grid dashboard-skills__grid--count-{Math.Clamp(SkillColumnCount, 1, 6)}";

        WeekPaceText = goal.CurrentWeekNumber is { } week
            ? $"Week {week}: {goal.WeekCompletedMinutes} of {goal.WeekPlannedMinutes} minutes done"
            : goal.TotalPathSteps > 0
                ? $"This path has no week plan · {goal.CompletedPathSteps} of {goal.TotalPathSteps} steps done"
                : "No active learning path yet";

        CascadeLabel = dashboard.NextAction.CascadeStep switch
        {
            "resume_in_progress" => "Step 1 · finish what is already open",
            "retry_unmeasured_skill" => "Step 2 · measure a skill nothing has measured",
            "next_step" => "Step 3 · the next step of your plan",
            "free_drill" => "Step 4 · the recommended practice",
            "daily_ceiling" => "Step 5 · today's practice is complete",
            "completion" => "Step 6 · the plan is complete",
            _ => "",
        };
    }

    private static int WeeksLeft(DateOnly today, DateOnly target) =>
        Math.Max(0, (int)Math.Ceiling((target.DayNumber - today.DayNumber) / 7.0));

    /// <summary>#159: the score as the certificate reports it. A certificate that stores tenths
    /// (IELTS, VSTEP) would otherwise print 65 for 6.5 on this card.</summary>
    public static string ScoreText(int? score, int factor) =>
        score.HasValue ? ScoreDisplay.Format(score.Value, factor) : "—";

    public static string SeverityBadge(string severity) => severity switch
    {
        "danger" => "dashboard-hero--danger",
        "warning" => "dashboard-hero--warning",
        "success" => "dashboard-hero--success",
        _ => "dashboard-hero--info",
    };
}
