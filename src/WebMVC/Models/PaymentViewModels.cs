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

/// <param name="LoadFailed">The API could not be read; <paramref name="Result"/> then only
/// carries the order code, and the page shows its "could not read order status" state.</param>
public sealed record PaymentResultViewModel(
    PaymentResultDto Result,
    bool LoadFailed = false);

public sealed record WalletViewModel(
    WalletDto Wallet);
