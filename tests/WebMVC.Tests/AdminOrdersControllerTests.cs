using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminOrdersControllerTests
{
    private static AdminOrdersSummaryDto SampleOrdersSummary() => new(
        Orders:
        [
            new(1, "DH-20261014-0001", 1001, 10, "learner@demo.local", "Learner Demo", 2, "Exam Prep", 5, 199000, "paid", "payos", "plink-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(2, "DH-20261014-0002", 1002, 11, "other@demo.local", "Other Learner", 1, "Pay as you go", 1, 49000, "reconciling", "mock", null, null, DateTimeOffset.UtcNow, null),
        ],
        TotalCount: 2,
        Page: 1,
        PageSize: 20,
        PaidThisMonthCount: 1,
        PaidThisMonthAmount: 199000,
        ReconcilingCount: 1,
        FailedOrExpiredCount: 0);

    private static AdminOrderDetailDto SampleOrderDetail(int orderId) => new(
        Order: new(orderId, "DH-20261014-0001", 1001, 10, "learner@demo.local", "Learner Demo", 2, "Exam Prep", 5, 199000, "paid", "payos", "plink-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        Events:
        [
            new(1, orderId, "return_redirect", "pending", false, false, "Browser return", null, DateTimeOffset.UtcNow.AddMinutes(-5)),
            new(2, orderId, "webhook", "paid", true, true, "Webhook paid", null, DateTimeOffset.UtcNow),
        ]);

    private static AdminOrdersController CreateController(TestOrdersApiClient api)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, "1"),
            new(IAccessTokenFactory.ClaimEmail, "admin@toeic.local"),
            new(IAccessTokenFactory.ClaimRole, Roles.Admin),
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "jwt", Value = "admin-token" }]);

        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminOrdersController(api, currentUser, NullLogger<AdminOrdersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };
    }

    [Fact]
    public async Task Index_returns_view_with_summary_and_selected_order()
    {
        var api = new TestOrdersApiClient
        {
            Summary = SampleOrdersSummary(),
            DetailToReturn = SampleOrderDetail(1),
        };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, gateway: null, page: 1, detailId: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminOrdersViewModel>(viewResult.Model);
        Assert.Equal(2, vm.Summary.Orders.Count);
        Assert.NotNull(vm.SelectedOrder);
        Assert.Equal(2, vm.SelectedOrder.Events.Count);
    }

    [Fact]
    public async Task Detail_returns_json_of_order_detail()
    {
        var api = new TestOrdersApiClient
        {
            DetailToReturn = SampleOrderDetail(1),
        };
        var controller = CreateController(api);

        var result = await controller.Detail(1, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var detail = Assert.IsType<AdminOrderDetailDto>(jsonResult.Value);
        Assert.Equal(1, detail.Order.OrderId);
        Assert.Equal(2, detail.Events.Count);
    }

    [Fact]
    public async Task Reconcile_calls_api_and_redirects_with_success_message()
    {
        var api = new TestOrdersApiClient
        {
            ReconcileResult = new PaymentResultDto(
                2, "DH-20261014-0002", "paid", "Pay as you go", 1, 49000, "mock", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1),
        };
        var controller = CreateController(api);

        var result = await controller.Reconcile(2, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminOrdersController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["Success"]);
        Assert.Equal(2, api.ReconciledOrderId);
    }

    [Fact]
    public async Task Index_on_api_failure_displays_error_and_renders_empty_summary()
    {
        var api = new TestOrdersApiClient { ShouldFail = true };
        var controller = CreateController(api);

        var result = await controller.Index(null, null, null, 1, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminOrdersViewModel>(viewResult.Model);
        Assert.Empty(vm.Summary.Orders);
        Assert.NotNull(controller.TempData["Error"]);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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

    private sealed class TestOrdersApiClient : ICompanionApiClient
    {
        public AdminOrdersSummaryDto? Summary { get; set; }
        public AdminOrderDetailDto? DetailToReturn { get; set; }
        public PaymentResultDto? ReconcileResult { get; set; }
        public int? ReconciledOrderId { get; set; }
        public bool ShouldFail { get; set; }

        public Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("Network failure");
            return Task.FromResult(Summary ?? new AdminOrdersSummaryDto([], 0, 1, 20, 0, 0, 0, 0));
        }

        public Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("Network failure");
            return Task.FromResult(DetailToReturn ?? SampleOrderDetail(orderId));
        }

        public Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default)
        {
            ReconciledOrderId = orderId;
            return Task.FromResult(ReconcileResult ?? new PaymentResultDto(
                orderId, "DH-20261014-0001", "paid", "Pkg", 5, 199000, "mock", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 5));
        }
    }
}
