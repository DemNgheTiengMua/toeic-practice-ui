using Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebMVC.Models;

public sealed record AdminRefundDetailViewModel(
    AdminRefundDetailDto Refund,
    string? ErrorMessage = null)
{
    public string StatusLabel { get; } = DisplayLabels.LabelFor(Refund.Status, NullLog);
    public string MethodLabel { get; } = DisplayLabels.LabelFor(Refund.Method, NullLog);
    public bool IsRequested { get; } = Refund.Status == RefundStatusDto.Requested;

    /// <summary>Money is never rendered here - this screen creates credit refunds only (#281, and
    /// #282 owns the money path) - so the only value on the page is the credit count, in the mono
    /// face the design system reserves for figures.</summary>
    public string CreditCount => Refund.CreditCount.ToString("N0");

    public string CreatedAt { get; init; } = "";
    public string? DecidedAt { get; init; }

    public AdminRefundDetailViewModel WithDates(DateTimeFormatter formatter) => this with
    {
        CreatedAt = formatter.FormatCustom(Refund.CreatedAt, "dd MMM yyyy, HH:mm"),
        DecidedAt = Refund.DecidedAt.HasValue
            ? formatter.FormatCustom(Refund.DecidedAt.Value, "dd MMM yyyy, HH:mm")
            : null,
    };

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog = NullLogger.Instance;
}
