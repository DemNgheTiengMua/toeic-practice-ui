using Contracts;

namespace WebMVC.Models;

public sealed record AdminUserDetailViewModel(AdminUserDetailDto Detail)
{
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();

    public AdminUserDetailViewModel WithDates(DateTimeFormatter formatter) => this with
    {
        Dates = FormattedDateViewModel.Prepare(
            Detail.Placements.Select(p => p.TakenAt),
            formatter)
    };
}
