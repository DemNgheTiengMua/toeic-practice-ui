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

    /// <summary>
    /// #220: the word each order's status reads as, so neither table renders a wire code. Both
    /// <c>Orders.cshtml</c> and its detail modal had a <c>switch</c> whose default arm printed
    /// <c>ToWireCode()</c> - the label now comes from one place and the switches are gone.
    /// </summary>
    public IReadOnlyDictionary<int, string> StatusLabels { get; } = Summary.Orders.ToDictionary(
        o => o.OrderId, o => DisplayLabels.LabelFor(o.Status, NullLog));

    /// <summary>The badge class for a status. A class is machine data, so it stays a class.</summary>
    public IReadOnlyDictionary<int, string> StatusClasses { get; } = Summary.Orders.ToDictionary(
        o => o.OrderId, o => o.Status switch
        {
            OrderStatusDto.Paid => "badge badge--success",
            OrderStatusDto.Reconciling => "badge badge--warning",
            OrderStatusDto.Failed => "badge badge--danger",
            _ => "badge",
        });

    /// <summary>#220: the payment-event source and reported status in words, for the detail modal.</summary>
    public IReadOnlyDictionary<int, string> EventSourceLabels { get; } = SelectedOrder?.Events.ToDictionary(
        e => e.EventId, e => DisplayLabels.LabelFor(e.Source, NullLog)) ?? new Dictionary<int, string>();

    public IReadOnlyDictionary<int, string?> EventReportedStatusLabels { get; } = SelectedOrder?.Events.ToDictionary(
        e => e.EventId, e => e.ReportedStatus is { } status ? DisplayLabels.LabelFor(status, NullLog) : (string?)null)
        ?? new Dictionary<int, string?>();
    public int FirstRow { get; } = (Summary.Page - 1) * Summary.PageSize + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public AdminOrdersViewModel WithDates(DateTimeFormatter formatter) => this with { Dates = FormattedDateViewModel.Prepare(
        Summary.Orders.Select(o => o.CreatedAt).Concat(SelectedOrder is null ? [] : new[] { SelectedOrder.Order.CreatedAt })
        .Concat(SelectedOrder?.Order.PaidAt is {} paid ? new[] { paid } : [])
        .Concat(SelectedOrder?.Events.Select(e => e.CreatedAt) ?? []), formatter) };

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
