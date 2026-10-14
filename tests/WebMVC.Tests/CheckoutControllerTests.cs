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

public sealed class CheckoutControllerTests
{
    private static CheckoutController CreateController(
        TestApiClient api,
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

        return new CheckoutController(api, currentUser, NullLogger<CheckoutController>.Instance)
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
    public async Task Index_renders_blocked_view_for_unverified_learner()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.EmailNotVerified, "Unverified", 403),
        };
        var controller = CreateController(api);

        var result = await controller.Index(1, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CheckoutViewModel>(view.Model);
        Assert.True(model.IsBlockedUnverified);
        Assert.Null(model.Preview);
    }

    [Fact]
    public async Task Index_renders_unfinished_order_when_open_order_exists()
    {
        var openOrder = new OrderDto(
            OrderId: 10,
            OrderCode: "DH-20261014-0010",
            PaymentNo: 10,
            PackageId: 2,
            PackageName: "Exam Prep",
            Credits: 5,
            Amount: 199000,
            Status: "pending",
            Gateway: "mock",
            GatewayTxnRef: "mock-10",
            GatewayErrorCode: null,
            CreatedAt: DateTimeOffset.UtcNow,
            PaidAt: null);

        var api = new TestApiClient
        {
            CheckoutPreview = new CheckoutPreviewDto(
                PackageId: 2,
                PackageName: "Exam Prep",
                Credits: 5,
                Price: 199000,
                CurrentBalance: 8,
                BalanceAfter: 13,
                OpenOrder: openOrder),
        };
        var controller = CreateController(api);

        var result = await controller.Index(2, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CheckoutViewModel>(view.Model);
        Assert.False(model.IsBlockedUnverified);
        Assert.NotNull(model.Preview);
        Assert.NotNull(model.Preview.OpenOrder);
        Assert.Equal("DH-20261014-0010", model.Preview.OpenOrder.OrderCode);
    }

    [Fact]
    public async Task Index_renders_order_confirmation_for_verified_learner()
    {
        var api = new TestApiClient
        {
            CheckoutPreview = new CheckoutPreviewDto(
                PackageId: 2,
                PackageName: "Exam Prep",
                Credits: 5,
                Price: 199000,
                CurrentBalance: 8,
                BalanceAfter: 13,
                OpenOrder: null),
        };
        var controller = CreateController(api);

        var result = await controller.Index(2, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CheckoutViewModel>(view.Model);
        Assert.False(model.IsBlockedUnverified);
        Assert.NotNull(model.Preview);
        Assert.Equal("Exam Prep", model.Preview.PackageName);
        Assert.Equal(199000, model.Preview.Price);
        Assert.Equal(8, model.Preview.CurrentBalance);
        Assert.Equal(13, model.Preview.BalanceAfter);
    }

    [Fact]
    public async Task CreateOrder_redirects_to_checkout_url()
    {
        var api = new TestApiClient
        {
            CreateOrderResponse = new CreateOrderResponse(
                OrderId: 10,
                OrderCode: "DH-20261014-0010",
                PaymentNo: 10,
                Amount: 199000,
                Gateway: "mock",
                CheckoutUrl: "/checkout/mock-gateway?orderCode=DH-20261014-0010",
                Status: "pending"),
        };
        var controller = CreateController(api);

        var result = await controller.CreateOrder(2, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/checkout/mock-gateway?orderCode=DH-20261014-0010", redirect.Url);
    }

    [Fact]
    public async Task CreateOrder_redirects_to_index_when_order_already_open()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.OrderAlreadyOpen, "Order already open", 409),
        };
        var controller = CreateController(api);

        var result = await controller.CreateOrder(2, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task MockGateway_renders_simulation_view_with_order_summary()
    {
        var api = new TestApiClient
        {
            PaymentResult = new PaymentResultDto(
                OrderId: 10,
                OrderCode: "DH-20261014-0010",
                Status: "pending",
                PackageName: "Exam Prep",
                Credits: 5,
                Amount: 199000,
                Gateway: "mock",
                CreatedAt: DateTimeOffset.UtcNow,
                PaidAt: null,
                CurrentBalance: 8),
        };
        var controller = CreateController(api);

        var result = await controller.MockGateway("DH-20261014-0010", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MockGatewayViewModel>(view.Model);
        Assert.Equal("DH-20261014-0010", model.OrderCode);
        Assert.Equal("Exam Prep", model.PackageName);
        Assert.Equal(199000, model.Amount);
    }

    [Fact]
    public async Task MockGatewayAction_submits_action_and_redirects_to_payment_result()
    {
        var api = new TestApiClient
        {
            PaymentResult = new PaymentResultDto(
                OrderId: 10,
                OrderCode: "DH-20261014-0010",
                Status: "paid",
                PackageName: "Exam Prep",
                Credits: 5,
                Amount: 199000,
                Gateway: "mock",
                CreatedAt: DateTimeOffset.UtcNow,
                PaidAt: DateTimeOffset.UtcNow,
                CurrentBalance: 13),
        };
        var controller = CreateController(api);

        var result = await controller.MockGatewayAction("DH-20261014-0010", "success", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/payment/result?orderCode=DH-20261014-0010", redirect.Url);
    }

    [Fact]
    public async Task Result_renders_payment_outcome()
    {
        var api = new TestApiClient
        {
            PaymentResult = new PaymentResultDto(
                OrderId: 10,
                OrderCode: "DH-20261014-0010",
                Status: "paid",
                PackageName: "Exam Prep",
                Credits: 5,
                Amount: 199000,
                Gateway: "mock",
                CreatedAt: DateTimeOffset.UtcNow,
                PaidAt: DateTimeOffset.UtcNow,
                CurrentBalance: 13),
        };
        var controller = CreateController(api);

        var result = await controller.Result("DH-20261014-0010", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PaymentResultViewModel>(view.Model);
        Assert.Equal("paid", model.Result.Status);
        Assert.Equal(5, model.Result.Credits);
        Assert.Equal(13, model.Result.CurrentBalance);
    }

    private sealed class FixedAuthentication(ClaimsPrincipal principal, AuthenticationProperties properties) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
    }

    private sealed class TestApiClient : ICompanionApiClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public CheckoutPreviewDto? CheckoutPreview { get; set; }
        public CreateOrderResponse? CreateOrderResponse { get; set; }
        public PaymentResultDto? PaymentResult { get; set; }

        public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(CheckoutPreview!);
        }

        public Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(CreateOrderResponse!);
        }

        public Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(PaymentResult!);
        }

        public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(PaymentResult!);
        }

        public Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(PaymentResult!);
        }

        public Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [], 8));
    }
}
