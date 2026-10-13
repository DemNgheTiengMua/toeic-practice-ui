namespace Contracts;

/// <summary>Wire DTOs for the learner's own context and dashboard (issue #7).</summary>
public sealed record CertificateOptionDto(string Code, string Name);

/// <summary>Everything the learner shell renders on every page: the active certificate, the ones
/// the switcher offers, the credit balance and the time zone dates are shown in.</summary>
public sealed record LearnerContextDto(
    CertificateOptionDto Active,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int CreditBalance,
    string TimeZoneId);

public sealed record SwitchCertificateRequest(string Code);

public sealed record LearnerDashboardDto(
    CertificateOptionDto Certificate,
    LatestResultDto? LatestResult,
    int AttemptCount,
    int PracticeCount,
    IReadOnlyList<RecentAttemptDto> RecentAttempts,
    IReadOnlyList<RecentPracticeDto> RecentPractice)
{
    public bool IsNew => AttemptCount == 0 && PracticeCount == 0;
}

public sealed record LatestResultDto(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);

public sealed record RecentAttemptDto(
    int AttemptId,
    string ExamName,
    string Purpose,
    string Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);

public sealed record RecentPracticeDto(
    int SessionId,
    string SectionName,
    string Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);
