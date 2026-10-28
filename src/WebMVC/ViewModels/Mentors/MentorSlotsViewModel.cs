using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed class MentorSlotsViewModel
{
    public MentorSummaryDto Mentor { get; set; } = null!;

    public IReadOnlyList<AdminMentorSlotDto> Slots { get; set; } = [];

    public CreateSlotViewModel NewSlot { get; set; } = new();

    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public MentorSlotsViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(Slots.SelectMany(s => new[] { s.StartAt, s.EndAt }), formatter);
        return this;
    }
}
