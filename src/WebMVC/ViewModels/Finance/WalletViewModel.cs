using Contracts;

namespace WebMVC.Models;

public sealed record WalletViewModel(
    WalletDto Wallet)
{
    public bool HasTransactions { get; } = Wallet.Orders.Count > 0 || Wallet.Ledger.Count > 0;
    public string AmountPurchased { get; } = CurrencyFormatter.FormatVnd(Wallet.AmountPurchased);
    public IReadOnlyDictionary<int, string> Amounts { get; } = Wallet.Orders.ToDictionary(o => o.OrderId, o => CurrencyFormatter.FormatVnd(o.Amount));
    public IReadOnlyDictionary<int, string> StatusClasses { get; } = Wallet.Orders.ToDictionary(o => o.OrderId, o => o.Status switch
    {
        OrderStatusDto.Paid => "badge badge--success", OrderStatusDto.Reconciling => "badge badge--warning",
        OrderStatusDto.Failed => "badge badge--danger", _ => "badge"
    });
    public IReadOnlyDictionary<int, string> StatusLabels { get; } = Wallet.Orders.ToDictionary(o => o.OrderId, o => o.Status switch
    {
        OrderStatusDto.Paid => "Paid", OrderStatusDto.Reconciling => "Reconciling", OrderStatusDto.Failed => "Failed",
        OrderStatusDto.Expired => "Expired", _ => "Pending"
    });
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public WalletViewModel WithDates(DateTimeFormatter formatter) => this with { Dates = FormattedDateViewModel.Prepare(
        Wallet.Orders.Select(o => o.CreatedAt).Concat(Wallet.Ledger.Select(e => e.CreatedAt)), formatter) };
    public IReadOnlyDictionary<int, string> LedgerDeltaLabels { get; } = Wallet.Ledger.ToDictionary(e => e.TxnId, e => e.Delta > 0 ? $"+{e.Delta}" : e.Delta.ToString());
    public IReadOnlyDictionary<int, string> LedgerDeltaClasses { get; } = Wallet.Ledger.ToDictionary(e => e.TxnId, e => e.Delta > 0 ? "text-success" : "text-danger");

}
