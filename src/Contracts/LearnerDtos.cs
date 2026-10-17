namespace Contracts;

/// <summary>Wire DTOs for the learner's own context and dashboard (issue #7).</summary>
public sealed record CertificateOptionDto(string Code, string Name);

/// <summary>Everything the learner shell renders on every page: the active certificate, the ones
/// the switcher offers and the credit balance. Dates render through DateTimeFormatter, which
/// loads the time zone itself (issue #6).</summary>
public sealed record LearnerContextDto(
    CertificateOptionDto Active,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int CreditBalance);

public sealed record SwitchCertificateRequest(string Code);

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

public sealed record LatestResultDto(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);

public sealed record RecentAttemptDto(
    int AttemptId,
    string ExamName,
    ExamPurposeDto Purpose,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);

public sealed record RecentPracticeDto(
    int SessionId,
    string SectionName,
    PracticeStatusDto Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);

public sealed record TargetReadinessDto(
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    string? CurrentSource,
    string? TargetBandCode,
    string? TargetBandName,
    int? TargetMinScore,
    int CompletedPathSteps,
    int TotalPathSteps);

public sealed record MissionStepDto(
    int StepNumber,
    string BadgeLabel,
    string Severity,
    string Eyebrow,
    string Title,
    string Description,
    string MetaLabel,
    string ActionLabel,
    string ActionUrl,
    bool IsCleared);

public sealed record SimulatorSkillDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    int BaseScaledScore,
    int MinScaledScore,
    int MaxScaledScore,
    int Step);

public sealed record ScoreBandThresholdDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);

public sealed record SimulatorConfigDto(
    int MaxTotalScore,
    int? CurrentTotalScore,
    string? CurrentBandCode,
    string? TargetBandCode,
    int? TargetMinScore,
    IReadOnlyList<SimulatorSkillDto> Skills,
    IReadOnlyList<ScoreBandThresholdDto> Bands);

public sealed record WeaknessStripDto(
    string Scope,
    int Id,
    string Code,
    string Name,
    string SectionName,
    int Answered,
    int Correct,
    int AccuracyPercent,
    string Tier,
    string ActionLabel,
    string ActionUrl);

public sealed record UpcomingBookingCardDto(
    int BookingId,
    string MentorName,
    string? MentorHeadline,
    string? FocusArea,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? MeetLink);

public sealed record RubricHighlightDto(
    string SkillName,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Comment,
    int AttemptId,
    ExamPurposeDto Purpose);
