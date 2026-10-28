using System.ComponentModel.DataAnnotations;
using Contracts;

namespace WebMVC.Models;

public sealed record MentorDetailViewModel(
    MentorDetailDto Detail,
    string? ErrorMessage = null)
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; private set; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public MentorDetailViewModel PrepareDates(DateTimeFormatter formatter)
    {
        Dates = FormattedDateViewModel.Prepare(Detail.AvailableSlots.Select(s => s.StartAt).Concat(Detail.Reviews.Select(r => r.CreatedAt)), formatter);
        return this;
    }
}
