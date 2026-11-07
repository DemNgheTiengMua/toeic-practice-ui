using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorBookingDetailViewModel(
    MentorBookingDto Booking,
    string? SuccessMessage = null,
    string? ErrorMessage = null)
{
    /// <summary>#223 / D21: the standing block, or null when this certificate cannot be described at all.</summary>
    public MentorStandingViewModel? Standing => Booking.Standing is { } standing ? new(standing) : null;

    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public MentorBookingDetailViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(new DateTimeOffset?[] { Booking.StartAt, Booking.CreatedAt, Booking.CancelledAt, Booking.Review?.CreatedAt }.OfType<DateTimeOffset>(), formatter);
        return this;
    }
}
