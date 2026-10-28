using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record CancelBookingResultDto(
    int BookingId,
    string Status,
    DateTimeOffset CancelledAt,
    int RefundedCredits);
