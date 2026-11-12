using Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebMVC.Models;

public sealed record AdminRefundsViewModel(
    AdminRefundsSummaryDto Summary,
    string? Query,
    string? Status,
    int CurrentPage)
{
    // #220 / ADR-0012: the words live in DisplayLabels, not here. A second copy of a vocabulary
    // drifts from the first, and this page would then name a status differently from the ledger row
    // the learner reads.
    public IReadOnlyDictionary<int, string> StatusLabels { get; } =
        Summary.Refunds.ToDictionary(r => r.RefundId, r => DisplayLabels.LabelFor(r.Status, NullLog));

    public IReadOnlyDictionary<int, string> MethodLabels { get; } =
        Summary.Refunds.ToDictionary(r => r.RefundId, r => DisplayLabels.LabelFor(r.Method, NullLog));

    /// <summary>#281: the badge class per status. Approved is green because the value landed;
    /// rejected is red because a refusal is a refused condition, not decoration (AGENTS.md §5).</summary>
    public IReadOnlyDictionary<int, string> StatusClasses { get; } =
        Summary.Refunds.ToDictionary(r => r.RefundId, r => r.Status switch
        {
            RefundStatusDto.Requested => "badge badge--warning",
            RefundStatusDto.Approved => "badge badge--success",
            RefundStatusDto.Settled => "badge badge--info",
            _ => "badge badge--danger",
        });

    /// <summary>
    /// #282: the value a row returns, in the unit its method names - dong for a money refund, credits
    /// for a credit one. Money goes through <see cref="CurrencyFormatter"/> and nothing else, so an
    /// amount on this page equals the amount in the database to the dong.
    /// </summary>
    public IReadOnlyDictionary<int, string> Values { get; } = Summary.Refunds.ToDictionary(
        r => r.RefundId,
        r => r.Method == RefundMethodDto.Money
            ? CurrencyFormatter.FormatVnd(r.Amount)
            : $"{r.CreditCount:N0} credits");

    public string CreditsRefunded => Summary.CreditsRefundedTotal.ToString("N0");
    public int FirstRow { get; } = (Summary.Page - 1) * Summary.PageSize + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog = NullLogger.Instance;

    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } =
        new Dictionary<DateTimeOffset, FormattedDateViewModel>();

    public AdminRefundsViewModel WithDates(DateTimeFormatter formatter) => this with
    {
        Dates = FormattedDateViewModel.Prepare(
            Summary.Refunds.SelectMany(r => r.SettledAt is { } settled ? new[] { r.CreatedAt, settled } : [r.CreatedAt]),
            formatter),
    };
}
