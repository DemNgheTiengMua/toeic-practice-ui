namespace BusinessObjects;

/// <summary>One certificate a learner can work on, as the switcher lists it.</summary>
public sealed record CertificateOption(string Code, string Name);

/// <summary>What every learner screen needs before it renders: the certificate it is scoped to,
/// the others the learner can switch to, and the credit balance (the ledger's sum — credits are
/// one wallet across certificates, so the balance is never scoped).</summary>
public sealed record LearnerContext(
    CertificateOption Active,
    IReadOnlyList<CertificateOption> Certificates,
    int CreditBalance,
    string TimeZoneId);

/// <summary>The learner's home page for their active certificate. Every list here belongs to
/// <see cref="Certificate"/> only; TOEIC history never shows while IELTS is active.</summary>
public sealed record LearnerDashboard(
    CertificateOption Certificate,
    LatestResult? LatestResult,
    int AttemptCount,
    int PracticeCount,
    IReadOnlyList<RecentAttempt> RecentAttempts,
    IReadOnlyList<RecentPractice> RecentPractice)
{
    /// <summary>No attempt and no practice yet for this certificate: the page shows its
    /// getting-started state instead of empty tables.</summary>
    public bool IsNew => AttemptCount == 0 && PracticeCount == 0;
}

/// <summary>The most recent graded mock attempt's score and band snapshot.</summary>
public sealed record LatestResult(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);

public sealed record RecentAttempt(
    int AttemptId,
    string ExamName,
    string Purpose,
    string Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);

public sealed record RecentPractice(
    int SessionId,
    string SectionName,
    string Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);
