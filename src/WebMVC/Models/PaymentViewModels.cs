using Contracts;

namespace WebMVC.Models;

public sealed record CheckoutViewModel(
    CheckoutPreviewDto? Preview,
    bool IsBlockedUnverified = false,
    string? ErrorMessage = null);

public sealed record MockGatewayViewModel(
    string OrderCode,
    string PackageName,
    int Credits,
    decimal Amount);

public sealed record PaymentResultViewModel(
    PaymentResultDto Result);

public sealed record WalletViewModel(
    WalletDto Wallet);
