using Contracts;
namespace WebMVC.Models;

public sealed class DashboardPresentation
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; }
    public LearnerDashboardDto Dashboard { get; }
    public AiInsightPanelViewModel InsightPanel { get; }
    public IReadOnlyDictionary<ExamPurposeDto, string> PurposeLabels { get; } = Enum.GetValues<ExamPurposeDto>().ToDictionary(x => x, PurposeLabel);
    public IReadOnlyDictionary<AttemptStatusDto, string> AttemptLabels { get; } = Enum.GetValues<AttemptStatusDto>().ToDictionary(x => x, AttemptStatusLabel);
    public IReadOnlyDictionary<PracticeStatusDto, string> PracticeLabels { get; } = Enum.GetValues<PracticeStatusDto>().ToDictionary(x => x, PracticeStatusLabel);
    public string? CurrentBandCode { get; }
    public string? CurrentBandName { get; }
    public int? CurrentScore { get; }
    public string? CurrentSource { get; }
    public int CompletedSteps { get; }
    public int TotalSteps { get; }
    public int PathPercent { get; }
    public int? TargetDeficit { get; }
    public IReadOnlyDictionary<int, DashboardAttemptLinks> AttemptLinks { get; }
    public string CertificateUrl { get; }
    public string Countdown { get; }
    public string Focus { get; }
    public IReadOnlyList<DashboardWeaknessViewModel> Strips { get; }
    public bool HasTestedStrips { get; }
    public int ClearedMissions { get; }
    public IReadOnlyDictionary<int, string> MissionBadges { get; }
    public DashboardSimulatorViewModel? Simulator { get; }
    public DashboardPresentation(LearnerDashboardDto dashboard, DateTimeFormatter dates, AiInsightDto? insight, DateTimeOffset now)
    {
        Dashboard = dashboard;
        var instants = dashboard.RecentAttempts.Select(a => a.StartedAt).Concat(dashboard.RecentPractice.Select(p => p.StartedAt)).ToList();
        if (dashboard.UnfinishedAttempt is { } a) instants.Add(a.StartedAt);
        if (dashboard.UnfinishedPractice is { } p) instants.Add(p.StartedAt);
        if (dashboard.UpcomingBooking is { } b) { instants.Add(b.StartAt); instants.Add(b.EndAt); }
        Dates = FormattedDateViewModel.Prepare(instants, dates);
        AttemptLinks = dashboard.RecentAttempts.Concat(dashboard.UnfinishedAttempt is { } unfinished ? new[] { unfinished } : Array.Empty<RecentAttemptDto>())
            .DistinctBy(attempt => attempt.AttemptId).ToDictionary(attempt => attempt.AttemptId, attempt => new DashboardAttemptLinks(attempt));
        CertificateUrl = $"/certificates#cert-{dashboard.Certificate.Code.ToLowerInvariant()}";
        var readiness = dashboard.TargetReadiness;
        CurrentBandCode = readiness?.CurrentBandCode ?? dashboard.LatestResult?.BandCode;
        CurrentBandName = readiness?.CurrentBandName ?? dashboard.LatestResult?.BandName;
        CurrentScore = readiness?.CurrentTotalScore ?? dashboard.LatestResult?.TotalScore;
        TargetDeficit = readiness?.TargetMinScore is { } target && CurrentScore is { } score ? Math.Max(0, target - score) : null;
        CurrentSource = readiness?.CurrentSource ?? (dashboard.LatestResult is not null ? "mock" : null);
        CompletedSteps = readiness?.CompletedPathSteps ?? 0;
        TotalSteps = readiness?.TotalPathSteps ?? 0;
        PathPercent = TotalSteps > 0 ? (int)Math.Round((double)CompletedSteps / TotalSteps * 100) : 0;
        Countdown = dashboard.UpcomingBooking is { } booking ? FormatCountdown(booking.StartAt, now) : "";
        Focus = string.IsNullOrWhiteSpace(dashboard.UpcomingBooking?.FocusArea) ? $"General {dashboard.Certificate.Code} coaching & diagnostic review" : dashboard.UpcomingBooking.FocusArea;
        Strips = dashboard.WeaknessStrips.Select(s => new DashboardWeaknessViewModel(s, dashboard.Certificate.Code)).ToArray();
        HasTestedStrips = dashboard.WeaknessStrips.Any(s => s.Answered > 0 && s.Tier != "UNTESTED");
        ClearedMissions = dashboard.MissionSteps.Count(s => s.IsCleared);
        MissionBadges = dashboard.MissionSteps.ToDictionary(s => s.StepNumber, s => s.Severity switch {
            "danger" => "badge--danger", "warning" => "badge--warning", "success" => "badge--success", "info" => "badge--info", _ => "badge--muted" });
        InsightPanel = new AiInsightPanelViewModel("dashboard", insight?.CertId ?? 0, insight, dashboard.LatestRubricHighlight, dashboard.OverallAccuracyPercent);
        Simulator = dashboard.SimulatorConfig is { Bands.Count: > 0, Skills.Count: > 0 } sim ? new DashboardSimulatorViewModel(dashboard, sim) : null;
    }
    public static string PurposeLabel(ExamPurposeDto purpose) => purpose switch
    {
        ExamPurposeDto.Mock => "Mock exam",
        ExamPurposeDto.Placement => "Placement test",
        ExamPurposeDto.SpeakingPractice => "Speaking practice",
        ExamPurposeDto.WritingPractice => "Writing practice",
        _ => purpose.ToWireCode(),
    };
    public static string AttemptStatusLabel(AttemptStatusDto status) => status switch
    {
        AttemptStatusDto.InProgress => "In progress",
        AttemptStatusDto.Submitted => "Submitted",
        AttemptStatusDto.Graded => "Graded",
        AttemptStatusDto.Abandoned => "Cancelled",
        _ => status.ToWireCode(),
    };
    public static string PracticeStatusLabel(PracticeStatusDto status) => status switch
    {
        PracticeStatusDto.InProgress => "In progress",
        PracticeStatusDto.Finished => "Finished",
        PracticeStatusDto.Abandoned => "Cancelled",
        _ => status.ToWireCode(),
    };
    public static string FormatCountdown(DateTimeOffset startAt, DateTimeOffset now)
    {
        var remaining = startAt - now;
        if (remaining.TotalMinutes <= 0)
        {
            return "STARTING NOW";
        }
        if (remaining.TotalDays >= 1)
        {
            var days = (int)Math.Floor(remaining.TotalDays);
            var hours = remaining.Hours;
            return $"IN {days}D {hours}H";
        }
        if (remaining.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(remaining.TotalHours);
            var mins = remaining.Minutes;
            return $"IN {hours}H {mins}M";
        }
        var totalMins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"IN {totalMins}M";
    }
}
