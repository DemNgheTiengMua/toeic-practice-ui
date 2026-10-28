
namespace Contracts;

public sealed record EligibleSubjectsDto(
    IReadOnlyList<ComplaintEligibleBookingDto> Bookings,
    IReadOnlyList<ComplaintEligibleAttemptDto> Attempts,
    IReadOnlyList<ComplaintEligibleOrderDto> Orders);
