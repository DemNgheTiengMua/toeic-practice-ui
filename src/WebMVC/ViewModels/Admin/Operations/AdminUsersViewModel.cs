using Contracts;

namespace WebMVC.Models;

public sealed record AdminUsersViewModel(
    AdminUsersSummaryDto Summary,
    string? Query,
    string? Status,
    int CurrentPage,
    AdminUserDto? AdjustUser = null,
    string? FormError = null)
{
    public int FirstRow { get; } = (Summary.Page - 1) * Summary.PageSize + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public AdminUsersViewModel WithDates(DateTimeFormatter formatter) => this with { Dates = FormattedDateViewModel.Prepare(
        Summary.Users.Where(u => u.LastLoginAt.HasValue).Select(u => u.LastLoginAt!.Value), formatter) };
}
