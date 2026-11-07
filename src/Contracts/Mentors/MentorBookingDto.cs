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
    MentorReviewDto? Review = null,
    /// <summary>
    /// #223 / D21: the learner's standing in this mentor's certificate, derived at read time from
    /// <c>LearnerSkillStates</c>. Null only for a certificate or learner that cannot be described at
    /// all; it is never omitted because nothing was measured - that state is
    /// <see cref="MentorLearnerStandingDto.HasNoMeasurement"/>.
    /// </summary>
    MentorLearnerStandingDto? Standing = null);
