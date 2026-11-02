
namespace Contracts;

/// <summary>#159: the learner's dashboard is three blocks. Every other block the ticket names was
/// deleted rather than hidden, so this DTO carries only what those three render.
/// <para>
/// <see cref="Skills"/> is the active certificate's own skill list (#149's rule): a certificate that
/// names its skills Comprehension and Expression gets those two cards, never a Listening/Reading pair.
/// </para></summary>
public sealed record LearnerDashboardDto(
    CertificateOptionDto Certificate,
    NextActionDto NextAction,
    GoalStandingDto Goal,
    IReadOnlyList<SkillCardDto> Skills,
    int AttemptCount,
    int PracticeCount,
    RecentAttemptDto? UnfinishedAttempt = null,
    RecentPracticeDto? UnfinishedPractice = null);
