
namespace Contracts;

/// <summary><paramref name="IsNew"/> is decided by the service (LearnerDashboard.IsNew) and sent
/// as data, so the rule for "show the getting-started state" lives in one place.</summary>
public sealed record LearnerDashboardDto(
    CertificateOptionDto Certificate,
    LatestResultDto? LatestResult,
    bool IsNew,
    int AttemptCount,
    int PracticeCount,
    IReadOnlyList<RecentAttemptDto> RecentAttempts,
    IReadOnlyList<RecentPracticeDto> RecentPractice,
    RecentAttemptDto? UnfinishedAttempt = null,
    RecentPracticeDto? UnfinishedPractice = null,
    TargetReadinessDto? TargetReadiness = null,
    IReadOnlyList<MissionStepDto>? MissionSteps = null,
    SimulatorConfigDto? SimulatorConfig = null,
    IReadOnlyList<WeaknessStripDto>? WeaknessStrips = null,
    UpcomingBookingCardDto? UpcomingBooking = null,
    RubricHighlightDto? LatestRubricHighlight = null,
    int? OverallAccuracyPercent = null)
{
    public IReadOnlyList<MissionStepDto> MissionSteps { get; init; } = MissionSteps ?? [];

    public IReadOnlyList<WeaknessStripDto> WeaknessStrips { get; init; } = WeaknessStrips ?? [];
}
