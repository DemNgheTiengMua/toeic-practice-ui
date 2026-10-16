using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class WalletControllerTests
{
    private static WalletController CreateController(
        IWalletClient api,
        bool isSignedIn = true,
        string role = Roles.Learner)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        var claims = new List<Claim>();
        if (isSignedIn)
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimUserId, "42"));
            claims.Add(new Claim(IAccessTokenFactory.ClaimEmail, "learner@demo.local"));
            claims.Add(new Claim(IAccessTokenFactory.ClaimRole, role));
        }

        var identity = isSignedIn ? new ClaimsIdentity(claims, "Cookies") : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "test-jwt-token" });

        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new WalletController(api, currentUser, NullLogger<WalletController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };
    }

    private sealed class EmptyTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task Index_renders_wallet_with_summary_orders_and_ledger()
    {
        var orders = new List<OrderDto>
        {
            new(1, "DH-20260920-0148", 148, 2, "Exam Prep", 5, 199000, OrderStatusDto.Paid, PaymentGatewayDto.Mock, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, "DH-20260920-0139", 139, 2, "Exam Prep", 5, 199000, OrderStatusDto.Expired, PaymentGatewayDto.Mock, null, null, DateTimeOffset.UtcNow.AddHours(-1), null),
        };

        var ledger = new List<CreditLedgerEntryDto>
        {
            new(101, CreditReasonDto.Purchase, 5, "Order DH-20260920-0148", DateTimeOffset.UtcNow, "DH-20260920-0148"),
            new(102, CreditReasonDto.MockExamStart, -1, "Mock exam charge", DateTimeOffset.UtcNow.AddMinutes(-30), null),
        };

        var wallet = new WalletDto(
            Balance: 4,
            CreditsPurchased: 5,
            CreditsUsed: 1,
            AmountPurchased: 199000,
            PaidOrderCount: 1,
            Orders: orders,
            Ledger: ledger);

        var controller = CreateController(new FakeWalletClient(wallet));

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WalletViewModel>(view.Model);
        Assert.Equal(4, model.Wallet.Balance);
        Assert.Equal(5, model.Wallet.CreditsPurchased);
        Assert.Equal(1, model.Wallet.CreditsUsed);
        Assert.Equal(199000, model.Wallet.AmountPurchased);
        Assert.Equal(1, model.Wallet.PaidOrderCount);
        Assert.Equal(2, model.Wallet.Orders.Count);
        Assert.Equal(2, model.Wallet.Ledger.Count);
    }

    private sealed class FakeWalletClient(WalletDto wallet) : IWalletClient
    {
        public Task<WalletDto> GetWalletAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(wallet);
    }
}
