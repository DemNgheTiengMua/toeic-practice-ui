namespace Contracts;

public sealed record UpcomingMentorBookingDto(
    int BookingId,
    int MentorId,
    string MentorName,
    string CertCode,
    string? SkillName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string MeetLink,
    string? FocusArea);

/// <summary>#159: the learner's dashboard is three blocks. Every other block the ticket names was
/// deleted rather than hidden, so this DTO carries only what those three render.
/// <para>
/// <b>#241 / LRN-11, LRN-12, LRN-13.</b> <paramref name="HasNoMeasurement"/> on
/// <see cref="Goal"/> is the switch: with nothing measured the page is one block and a sentence about
/// placement, not three blocks of empty figures. Which blocks those are is a presentation decision
/// and lives in <c>DashboardViewModel.Blocks</c>, so this DTO stays a wire shape.
/// </para>
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
    RecentPracticeDto? UnfinishedPractice = null,
    IReadOnlyList<UpcomingMentorBookingDto>? UpcomingBookings = null);
