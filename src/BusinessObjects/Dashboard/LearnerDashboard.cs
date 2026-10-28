
namespace BusinessObjects;

/// <summary>The learner's home page for their active certificate. Every list here belongs to
/// <see cref="Certificate"/> only; TOEIC history never shows while IELTS is active.</summary>
public sealed record LearnerDashboard(
    CertificateOption Certificate,
    LatestResult? LatestResult,
    int AttemptCount,
    int PracticeCount,
    IReadOnlyList<RecentAttempt> RecentAttempts,
    IReadOnlyList<RecentPractice> RecentPractice,
    RecentAttempt? UnfinishedAttempt = null,
    RecentPractice? UnfinishedPractice = null,
    TargetReadiness? TargetReadiness = null,
    IReadOnlyList<MissionStep>? MissionSteps = null,
    SimulatorConfig? SimulatorConfig = null,
    IReadOnlyList<WeaknessStrip>? WeaknessStrips = null,
    UpcomingBookingCard? UpcomingBooking = null,
    RubricHighlight? LatestRubricHighlight = null,
    int? OverallAccuracyPercent = null)
{
    public IReadOnlyList<MissionStep> MissionSteps { get; init; } = MissionSteps ?? [];

    public IReadOnlyList<WeaknessStrip> WeaknessStrips { get; init; } = WeaknessStrips ?? [];

    /// <summary>No attempt and no practice yet for this certificate: the page shows its
    /// getting-started state instead of empty tables.</summary>
    public bool IsNew => AttemptCount == 0 && PracticeCount == 0;
}
