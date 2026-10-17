namespace BusinessObjects;

/// <summary>One certificate a learner can work on, as the switcher lists it.</summary>
public sealed record CertificateOption(string Code, string Name);

/// <summary>What every learner screen needs before it renders: the certificate it is scoped to,
/// the others the learner can switch to, and the credit balance (the ledger's sum — credits are
/// one wallet across certificates, so the balance is never scoped).</summary>
public sealed record LearnerContext(
    CertificateOption Active,
    IReadOnlyList<CertificateOption> Certificates,
    int CreditBalance);

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

/// <summary>The most recent graded mock attempt's score and band snapshot.</summary>
public sealed record LatestResult(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);

public sealed record RecentAttempt(
    int AttemptId,
    string ExamName,
    ExamPurpose Purpose,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);

public sealed record RecentPractice(
    int SessionId,
    string SectionName,
    PracticeStatus Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);

public sealed record TargetReadiness(
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    string? CurrentSource,
    string? TargetBandCode,
    string? TargetBandName,
    int? TargetMinScore,
    int CompletedPathSteps,
    int TotalPathSteps);

public sealed record MissionStep(
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

public sealed record SimulatorSkill(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    int BaseScaledScore,
    int MinScaledScore,
    int MaxScaledScore,
    int Step);

public sealed record ScoreBandThreshold(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);

public sealed record SimulatorConfig(
    int MaxTotalScore,
    int? CurrentTotalScore,
    string? CurrentBandCode,
    string? TargetBandCode,
    int? TargetMinScore,
    IReadOnlyList<SimulatorSkill> Skills,
    IReadOnlyList<ScoreBandThreshold> Bands);

public sealed record WeaknessStrip(
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

public sealed record UpcomingBookingCard(
    int BookingId,
    string MentorName,
    string? MentorHeadline,
    string? FocusArea,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? MeetLink);

public sealed record RubricHighlight(
    string SkillName,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Comment,
    int AttemptId,
    ExamPurpose Purpose);
