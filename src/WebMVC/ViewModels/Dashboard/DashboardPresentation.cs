using Contracts;
using WebMVC.Formatters;

namespace WebMVC.Models;

/// <summary>#159: the three-block dashboard, prepared for rendering. Every value here is a
/// presentation decision (a label, a link, a formatted date); no number is recomputed, so the page
/// cannot disagree with the service that measured it.</summary>
public sealed class DashboardPresentation
{
    public LearnerDashboardDto Dashboard { get; }
    public string CertificateUrl { get; }
    public string TargetDateDisplay { get; }
    public string WeekPaceText { get; }
    public string WeekProgressPercent { get; }
    public string GoalStatusLabel { get; }
    public string GoalStatusBadge { get; }

    public DashboardPresentation(LearnerDashboardDto dashboard, DateTimeFormatter dates)
    {
        Dashboard = dashboard;
        CertificateUrl = $"/certificates#cert-{dashboard.Certificate.Code.ToLowerInvariant()}";

        var goal = dashboard.Goal;
        TargetDateDisplay = goal.TargetDate is { } target
            ? target.ToString("d MMM yyyy")
            : "";

        WeekPaceText = goal.CurrentWeekNumber is { } week
            ? $"Week {week}: {goal.WeekCompletedMinutes} of {goal.WeekPlannedMinutes} min planned"
            : goal.TotalPathSteps > 0
                ? "This path has no week plan"
                : "No active learning path";

        WeekProgressPercent = goal.WeekPlannedMinutes > 0
            ? $"{Math.Min(100, (int)Math.Round((double)goal.WeekCompletedMinutes / goal.WeekPlannedMinutes * 100))}%"
            : "0%";

        // The verdict is the stored one, worded for a learner. A goal with no answer is not a
        // refusal: it means the certificate's bands carry no study hours, or there is no deadline.
        (GoalStatusLabel, GoalStatusBadge) = goal.Status switch
        {
            "feasible" => ("On pace for the date", "badge--success"),
            "stretch" => ("Achievable with more hours a week", "badge--warning"),
            "unrealistic" => ("Not achievable before the date", "badge--danger"),
            _ => ("", "badge--muted"),
        };
    }

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
