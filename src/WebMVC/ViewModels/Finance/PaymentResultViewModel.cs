using Contracts;

namespace WebMVC.Models;

/// <param name="LoadFailed">The API could not be read; <paramref name="Result"/> then only
/// carries the order code, and the page shows its "could not read order status" state.</param>
public sealed record PaymentResultViewModel(
    PaymentResultDto Result,
    bool LoadFailed = false)
{
    public string Amount { get; } = CurrencyFormatter.FormatVnd(Result.Amount);
    public bool IsPaid { get; } = !LoadFailed && Result.Status == OrderStatusDto.Paid;
    public bool IsFailed { get; } = !LoadFailed && Result.Status == OrderStatusDto.Failed;
    public bool IsExpired { get; } = !LoadFailed && Result.Status == OrderStatusDto.Expired;
    public bool IsReconciling { get; } = !LoadFailed && Result.Status is OrderStatusDto.Reconciling or OrderStatusDto.Pending;
    public string PaymentTime { get; init; } = "";
    public string CreatedTime { get; init; } = "";
    public PaymentResultViewModel WithDates(DateTimeFormatter formatter) => this with {
        PaymentTime = formatter.FormatCustom(Result.PaidAt ?? Result.CreatedAt, "dd MMM yyyy HH:mm"),
        CreatedTime = formatter.FormatCustom(Result.CreatedAt, "HH:mm")
    };

}
