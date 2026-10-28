using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorConfirmViewModel(
    MentorSummaryDto Mentor,
    MentorSlotDto? SelectedSlot,
    int CreditBalance,
    string? FocusArea = null,
    int? ExamId = null,
    string? ErrorMessage = null)
{
    public bool IsPaywall => CreditBalance < Mentor.PriceCredits;
    public int ShortBy => Math.Max(0, Mentor.PriceCredits - CreditBalance);
    public int BalanceAfterBooking => Math.Max(0, CreditBalance - Mentor.PriceCredits);

    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public MentorConfirmViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(SelectedSlot is { } slot ? new[] { slot.StartAt } : Array.Empty<DateTimeOffset>(), formatter);
        return this;
    }
}
