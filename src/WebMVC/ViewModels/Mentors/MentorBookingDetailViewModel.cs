using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorBookingDetailViewModel(
    MentorBookingDto Booking,
    string? SuccessMessage = null,
    string? ErrorMessage = null)
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public MentorBookingDetailViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(new DateTimeOffset?[] { Booking.StartAt, Booking.CreatedAt, Booking.CancelledAt, Booking.Review?.CreatedAt }.OfType<DateTimeOffset>(), formatter);
        return this;
    }
}
