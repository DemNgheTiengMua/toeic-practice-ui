namespace Contracts;

public sealed record AdminMentorPayoutItemDto(
    int BookingId,
    DateOnly SessionDate,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int MentorId,
    string MentorName,
    string LearnerName,
    string LearnerEmail,
    string CertCode,
    string? SkillName,
    int PriceCredits,
    decimal AmountVnd);

public sealed record AdminMentorPayoutMentorSummaryDto(
    int MentorId,
    string MentorName,
    string CertCode,
    int CompletedSessionsCount,
    int TotalCredits,
    decimal TotalAmountVnd);

public sealed record AdminMentorPayoutsReportDto(
    DateOnly From,
    DateOnly To,
    decimal VndPerCredit,
    int TotalSessions,
    int TotalCredits,
    decimal TotalAmountVnd,
    IReadOnlyList<AdminMentorPayoutMentorSummaryDto> Mentors,
    IReadOnlyList<AdminMentorPayoutItemDto> Sessions);
