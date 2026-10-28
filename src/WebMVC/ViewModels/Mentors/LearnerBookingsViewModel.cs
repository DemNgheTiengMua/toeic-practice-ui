using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record LearnerBookingsViewModel(
    IReadOnlyList<MentorBookingDto> Bookings,
    string? ErrorMessage = null)
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public LearnerBookingsViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(Bookings.SelectMany(b => new DateTimeOffset?[] { b.StartAt, b.CreatedAt }).OfType<DateTimeOffset>(), formatter);
        return this;
    }
}
