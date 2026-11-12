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

    /// <summary>A money refund is settled by the accountant, and only once approved: that is the
    /// moment the money counts (ADR-0014), so the action appears exactly then (#282).</summary>
    public bool CanSettle { get; } =
        Refund.Status == RefundStatusDto.Approved && Refund.Method == RefundMethodDto.Money;

    public bool IsMoney { get; } = Refund.Method == RefundMethodDto.Money;

    /// <summary>The credit count, in the mono face the design system reserves for figures.</summary>
    public string CreditCount => Refund.CreditCount.ToString("N0");

    /// <summary>A money refund's value, formatted only through <see cref="CurrencyFormatter"/> so it
    /// states the exact stored amount.</summary>
    public string Amount => CurrencyFormatter.FormatVnd(Refund.Amount);

    /// <summary>What the order behind this refund was paid, when there is one: the ceiling an
    /// accountant checks the request against.</summary>
    public string? OrderPaidAmount => Refund.OrderPaidAmount is { } paid
        ? CurrencyFormatter.FormatVnd(paid)
        : null;

    public string CreatedAt { get; init; } = "";
    public string? DecidedAt { get; init; }
    public string? SettledAt { get; init; }

    public AdminRefundDetailViewModel WithDates(DateTimeFormatter formatter) => this with
    {
        CreatedAt = formatter.FormatCustom(Refund.CreatedAt, "dd MMM yyyy, HH:mm"),
        DecidedAt = Refund.DecidedAt.HasValue
            ? formatter.FormatCustom(Refund.DecidedAt.Value, "dd MMM yyyy, HH:mm")
            : null,
        SettledAt = Refund.SettledAt.HasValue
            ? formatter.FormatCustom(Refund.SettledAt.Value, "dd MMM yyyy, HH:mm")
            : null,
    };

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog = NullLogger.Instance;
}
