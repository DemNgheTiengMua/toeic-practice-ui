using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record LearnerBookingsViewModel(
    IReadOnlyList<MentorBookingDto> Bookings,
    string? ErrorMessage = null)
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();

    /// <summary>
    /// #220: the word each booking's status reads as. The view used to print <c>@b.Status</c> in its
    /// final branch - the one status nobody had written a case for - so the leak was exactly the
    /// status the page's author had not anticipated.
    /// </summary>
    public IReadOnlyDictionary<int, string> StatusLabels { get; } = Bookings.ToDictionary(
        b => b.BookingId,
        b => DisplayLabels.LabelForCode<BookingStatusDto>(b.Status, NullLog));

    public LearnerBookingsViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(Bookings.SelectMany(b => new DateTimeOffset?[] { b.StartAt, b.CreatedAt }).OfType<DateTimeOffset>(), formatter);
        return this;
    }

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
