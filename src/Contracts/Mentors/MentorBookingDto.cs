using System.ComponentModel.DataAnnotations;

namespace Contracts;

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
    string? RedirectUrl = null,
    MentorReviewDto? Review = null);
