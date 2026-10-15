namespace Contracts;

public sealed record MentorSummaryDto(
    int MentorId,
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    string CertCode,
    string CertName,
    int? SkillId,
    string? SkillCode,
    string? SkillName,
    string? Modality,
    string Type,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive,
    decimal? AvgRating,
    int ReviewCount);

public sealed record MentorSlotDto(
    int SlotId,
    int MentorId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status);

public sealed record MentorReviewDto(
    int ReviewId,
    int Rating,
    string? Text,
    string UserDisplayName,
    DateTimeOffset CreatedAt);

public sealed record MentorDetailDto(
    MentorSummaryDto Mentor,
    IReadOnlyList<MentorSlotDto> AvailableSlots,
    IReadOnlyList<MentorReviewDto> Reviews);

public sealed record AdminMentorListItemDto(
    int MentorId,
    int? UserId,
    string DisplayName,
    string Type,
    int CertId,
    string CertCode,
    int? SkillId,
    string? SkillName,
    decimal PricePerSlot,
    int PriceCredits,
    int SessionMinutes,
    decimal? AvgRating,
    int ReviewCount,
    int TotalSlotsCount,
    int OpenSlotsCount,
    bool IsActive);

public sealed record AdminMentorSlotDto(
    int SlotId,
    int MentorId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status);

public sealed record CreateMentorProfileRequest(
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    int? SkillId,
    string Type,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive);

public sealed record UpdateMentorProfileRequest(
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    int? SkillId,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive);

public sealed record CreateMentorSlotRequest(
    DateTime LocalStartAt,
    DateTime? LocalEndAt,
    string? TimeZoneId = null);

public sealed record MentorSkillOptionDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    string Modality);

public sealed record MentorCertificateOptionDto(
    int CertId,
    string Code,
    string Name,
    IReadOnlyList<MentorSkillOptionDto> Skills);

public sealed record BookMentorRequest(
    int? SlotId = null,
    string? FocusArea = null,
    int? ExamId = null);

public sealed record MentorBookingDto(
    int BookingId,
    int UserId,
    int MentorId,
    string MentorName,
    string? MentorHeadline,
    string MentorType,
    int? SlotId,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string Status,
    string? MeetLink,
    string? FocusArea,
    int PriceCredits,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    int? AttemptId = null,
    string? RedirectUrl = null);

public sealed record CancelBookingResultDto(
    int BookingId,
    string Status,
    DateTimeOffset CancelledAt,
    int RefundedCredits);
