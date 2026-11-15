namespace Contracts;

/// <summary>
/// #381: what staff need to see a booking before it is over. Deliberately separate from
/// <see cref="AdminMentorPayoutItemDto"/>, which describes only delivered, payable sessions and
/// carries no status - the whole point here is pending, confirmed and cancelled.
/// </summary>
public sealed record AdminMentorBookingDto(
    int BookingId,
    int MentorId,
    string MentorName,
    int LearnerUserId,
    string LearnerName,
    string LearnerEmail,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string? FocusArea,
    int PriceCredits,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt);
