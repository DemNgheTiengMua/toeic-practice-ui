using System;
using Contracts;
using Microsoft.AspNetCore.Http;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class IdentityFinanceDisplayTests
{
    private static DateTimeFormatter Dates()
    {
        var context = new DefaultHttpContext();
        context.Items[DateTimeFormatter.ZoneItemKey] = "Asia/Ho_Chi_Minh";
        return new DateTimeFormatter(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Wallet_PreparesOrdersAndLedgerInRequestTimeZone_WithoutChangingBalance()
    {
        var instant = new DateTimeOffset(2026, 10, 26, 0, 30, 0, TimeSpan.Zero);
        var wallet = new WalletDto(5, 8, 3, 199000, 1,
            [new OrderDto(1, "O1", 1, 2, "Starter", 8, 199000, OrderStatusDto.Paid, PaymentGatewayDto.Payos, null, null, instant, instant, null)],
            [new CreditLedgerEntryDto(1, CreditReasonDto.Purchase, 8, null, instant, "O1"),
             new CreditLedgerEntryDto(2, CreditReasonDto.MockExamStart, -3, null, instant, null)]);
        var model = new WalletViewModel(wallet).WithDates(Dates());
        Assert.Same(wallet, model.Wallet);
        Assert.Equal(5, model.Wallet.Balance);
        Assert.Equal("26 Oct 2026, 07:30", model.Dates[instant].Full);
        Assert.Equal("Paid", model.StatusLabels[1]);
        Assert.Equal("badge badge--success", model.StatusClasses[1]);
        Assert.Equal("+8", model.LedgerDeltaLabels[1]);
        Assert.Equal("-3", model.LedgerDeltaLabels[2]);
    }

    [Fact]
    public void EmptyWallet_HasNoTransactionsAndNeedsNoDateRows()
    {
        var model = new WalletViewModel(new WalletDto(0, 0, 0, 0, 0, [], [])).WithDates(Dates());
        Assert.False(model.HasTransactions);
        Assert.Empty(model.Dates);
    }

    [Theory]
    [InlineData(OrderStatusDto.Paid, false, true, false)]
    [InlineData(OrderStatusDto.Pending, false, false, true)]
    [InlineData(OrderStatusDto.Reconciling, false, false, true)]
    [InlineData(OrderStatusDto.Paid, true, false, false)]
    public void PaymentResult_LoadFailureNeverShowsSuccessOrKeepsPolling(
        OrderStatusDto status, bool loadFailed, bool paid, bool polling)
    {
        var instant = new DateTimeOffset(2026, 10, 26, 0, 30, 0, TimeSpan.Zero);
        var result = new PaymentResultDto(1, "O1", status, "Starter", 5, 199000, PaymentGatewayDto.Mock, instant, null, 5);
        var model = new PaymentResultViewModel(result, loadFailed).WithDates(Dates());
        Assert.Equal(paid, model.IsPaid);
        Assert.Equal(polling, model.IsReconciling);
        Assert.Equal("26 Oct 2026 07:30", model.PaymentTime);
    }

    [Fact]
    public void Checkout_PreservesRawPackageNameAndFormatsOpenOrderInRequestTimeZone()
    {
        var instant = new DateTimeOffset(2026, 10, 26, 0, 30, 0, TimeSpan.Zero);
        var order = new OrderDto(1, "O1", 1, 2, "Starter", 5, 199000, OrderStatusDto.Pending, PaymentGatewayDto.Payos, null, null, instant, null, null);
        var preview = new CheckoutPreviewDto(2, "Package \"<script>\" & friends", 5, 199000, 3, 8, order);
        var model = new CheckoutViewModel(preview).WithDates(Dates());
        Assert.Equal("Package \"<script>\" & friends", model.PackageName);
        Assert.Equal("07:30", model.OpenOrderTime);
        Assert.Same(order, model.Preview!.OpenOrder);
        Assert.Equal(8, model.Preview.BalanceAfter);
    }
}
