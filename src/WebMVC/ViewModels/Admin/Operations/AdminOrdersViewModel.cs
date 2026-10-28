using Contracts;

namespace WebMVC.Models;

public sealed record AdminOrdersViewModel(
    AdminOrdersSummaryDto Summary,
    string? Query,
    string? Status,
    string? Gateway,
    int CurrentPage,
    AdminOrderDetailDto? SelectedOrder = null)
{
    public string PaidAmount { get; } = CurrencyFormatter.FormatVnd(Summary.PaidThisMonthAmount);
    public string SelectedAmount { get; } = SelectedOrder is null ? "" : CurrencyFormatter.FormatVnd(SelectedOrder.Order.Amount);
    public IReadOnlyDictionary<int, string> Amounts { get; } = Summary.Orders.ToDictionary(o => o.OrderId, o => CurrencyFormatter.FormatVnd(o.Amount));
    public int FirstRow { get; } = (Summary.Page - 1) * Summary.PageSize + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public AdminOrdersViewModel WithDates(DateTimeFormatter formatter) => this with { Dates = FormattedDateViewModel.Prepare(
        Summary.Orders.Select(o => o.CreatedAt).Concat(SelectedOrder is null ? [] : new[] { SelectedOrder.Order.CreatedAt })
        .Concat(SelectedOrder?.Order.PaidAt is {} paid ? new[] { paid } : [])
        .Concat(SelectedOrder?.Events.Select(e => e.CreatedAt) ?? []), formatter) };
}
