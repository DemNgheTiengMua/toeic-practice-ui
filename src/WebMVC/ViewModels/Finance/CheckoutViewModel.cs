using Contracts;

namespace WebMVC.Models;

public sealed record CheckoutViewModel(
    CheckoutPreviewDto? Preview,
    bool IsBlockedUnverified = false,
    string? ErrorMessage = null)
{
    public string PackageName { get; } = Preview?.PackageName ?? "Credit Package";
    public string Price { get; } = Preview is null ? "" : CurrencyFormatter.FormatVnd(Preview.Price);
    public int Credits { get; } = Preview?.Credits ?? 0;
    public string OpenOrderAmount { get; } = Preview?.OpenOrder is null ? "" : CurrencyFormatter.FormatVnd(Preview.OpenOrder.Amount);
    public string OpenOrderTime { get; init; } = "";
    public CheckoutViewModel WithDates(DateTimeFormatter formatter) => this with
    {
        OpenOrderTime = Preview?.OpenOrder is null ? "" : formatter.FormatCustom(Preview.OpenOrder.CreatedAt, "HH:mm")
    };
}
