using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminControllerTests
{
    private static AdminDashboardDto SampleDashboard() => new(
        ActiveLearnersCount: 1284,
        NewLearners7DaysCount: 47,
        CreditsUsedThisMonth: 3912,
        CreditsRefundedThisMonth: 18,
        CreditsPerDayAverage: 130.4m,
        RevenueTodayVnd: 1250000m,
        RevenueThisMonthVnd: 64700000m,
        PaidOrdersThisMonthCount: 328,
        MostAttemptedExams:
        [
            new(1, "ETS 2024 — Test 1", 612, 705m),
            new(2, "ETS 2024 — Test 2", 548, 688m),
            new(3, "ETS 2023 — Test 5", 431, 722m),
            new(4, "Economy Vol.1 — Test 4", 287, 651m),
        ],
        NeedsAttention: new AdminNeedsAttentionDto(
            DraftExamsCount: 2,
            StalePendingOrdersCount: 5,
            OpenComplaintsCount: 1));

    private static AdminDashboardDto EmptyDashboard() => AdminDashboardDto.Empty;

    private static AdminController CreateController(TestAdminApiClient api)
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

        return new AdminController(api, currentUser, NullLogger<AdminController>.Instance)
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
    public async Task Index_returns_view_with_populated_dashboard_figures()
    {
        var expected = SampleDashboard();
        var api = new TestAdminApiClient { DashboardToReturn = expected };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WebMVC.Models.AdminDashboardViewModel>(viewResult.Model).Dashboard;

        Assert.Equal(1284, model.ActiveLearnersCount);
        Assert.Equal(47, model.NewLearners7DaysCount);
        Assert.Equal(3912, model.CreditsUsedThisMonth);
        Assert.Equal(130.4m, model.CreditsPerDayAverage);
        Assert.Equal(1250000m, model.RevenueTodayVnd);
        Assert.Equal(64700000m, model.RevenueThisMonthVnd);
        Assert.Equal(328, model.PaidOrdersThisMonthCount);
        Assert.Equal(4, model.MostAttemptedExams.Count);
        Assert.Equal("ETS 2024 — Test 1", model.MostAttemptedExams[0].ExamName);
        Assert.Equal(612, model.MostAttemptedExams[0].AttemptsCount);
        Assert.Equal(705m, model.MostAttemptedExams[0].AverageScore);
        Assert.Equal(2, model.NeedsAttention.DraftExamsCount);
        Assert.Equal(5, model.NeedsAttention.StalePendingOrdersCount);
        Assert.Equal(1, model.NeedsAttention.OpenComplaintsCount);
    }

    /// <summary>
    /// #284: a negative figure is a fact, and the screen must say so twice - the formatter emits a true
    /// minus sign, and the value carries the refusal colour. The pair matters: a red zero, or a plain
    /// negative, is a screen an owner reads as a broken number.
    /// </summary>
    [Fact]
    public void A_negative_revenue_renders_a_true_minus_sign_and_the_danger_class()
    {
        var viewModel = new WebMVC.Models.AdminDashboardViewModel(
            AdminDashboardDto.Empty with { RevenueTodayVnd = -250000m, RevenueThisMonthVnd = -180000m });

        Assert.Equal("\u2212250,000 ₫", viewModel.RevenueToday);
        Assert.Equal("\u2212180,000 ₫", viewModel.RevenueThisMonth);
        Assert.Equal("stat__value text-danger", viewModel.RevenueTodayClass);
        Assert.Equal("stat__value text-danger", viewModel.RevenueThisMonthClass);
    }

    /// <summary>
    /// The other half of the same rule: a positive figure must render exactly as it did before #284. A
    /// screen that painted every revenue figure red would pass a test that only looked at the negative one.
    /// </summary>
    [Fact]
    public void A_positive_revenue_keeps_its_plain_rendering()
    {
        var viewModel = new WebMVC.Models.AdminDashboardViewModel(
            AdminDashboardDto.Empty with { RevenueTodayVnd = 1250000m, RevenueThisMonthVnd = 64700000m });

        Assert.Equal("1,250,000 ₫", viewModel.RevenueToday);
        Assert.Equal("64,700,000 ₫", viewModel.RevenueThisMonth);
        Assert.Equal("stat__value", viewModel.RevenueTodayClass);
        Assert.Equal("stat__value", viewModel.RevenueThisMonthClass);
    }

    /// <summary>
    /// #284: the view model decides the colour, and this proves the view actually uses that decision. A
    /// screen that formatted the minus sign but rendered every figure in the plain class would pass the two
    /// tests above; this is the test that catches it.
    /// </summary>
    [Fact]
    public void The_dashboard_renders_both_revenue_figures_through_their_class()
    {
        var view = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Admin", "Index.cshtml"));

        Assert.Contains("class=\"@Model.RevenueTodayClass\"", view);
        Assert.Contains("class=\"@Model.RevenueThisMonthClass\"", view);

        // And no inline style or hard-coded colour: the design system owns the palette (§5).
        Assert.DoesNotContain("style=", view);
        Assert.DoesNotContain("#b31217", view);
    }

    [Fact]
    public async Task Index_returns_view_with_empty_state_cleanly()
    {
        var empty = EmptyDashboard();
        var api = new TestAdminApiClient { DashboardToReturn = empty };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WebMVC.Models.AdminDashboardViewModel>(viewResult.Model).Dashboard;

        Assert.Equal(0, model.ActiveLearnersCount);
        Assert.Equal(0, model.NewLearners7DaysCount);
        Assert.Equal(0, model.CreditsUsedThisMonth);
        Assert.Equal(0m, model.CreditsPerDayAverage);
        Assert.Equal(0m, model.RevenueThisMonthVnd);
        Assert.Equal(0, model.PaidOrdersThisMonthCount);
        Assert.Empty(model.MostAttemptedExams);
        Assert.Equal(0, model.NeedsAttention.DraftExamsCount);
        Assert.Equal(0, model.NeedsAttention.StalePendingOrdersCount);
        Assert.Equal(0, model.NeedsAttention.OpenComplaintsCount);
    }

    [Fact]
    public async Task Index_on_api_failure_displays_error_and_renders_empty_dashboard()
    {
        var api = new TestAdminApiClient { ShouldFail = true };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WebMVC.Models.AdminDashboardViewModel>(viewResult.Model).Dashboard;

        Assert.Equal(0, model.ActiveLearnersCount);
        Assert.Equal(0m, model.RevenueThisMonthVnd);
        Assert.Empty(model.MostAttemptedExams);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Index_when_api_returns_null_renders_empty_dashboard()
    {
        var api = new TestAdminApiClient { ReturnNull = true };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WebMVC.Models.AdminDashboardViewModel>(viewResult.Model).Dashboard;

        Assert.Equal(0, model.ActiveLearnersCount);
        Assert.Equal(0m, model.RevenueThisMonthVnd);
        Assert.Empty(model.MostAttemptedExams);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestAdminApiClient : IAdminDashboardClient
    {
        public AdminDashboardDto? DashboardToReturn { get; set; }
        public bool ShouldFail { get; set; }
        public bool ReturnNull { get; set; }

        public Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("API failure");
            if (ReturnNull) return Task.FromResult<AdminDashboardDto>(null!);
            return Task.FromResult(DashboardToReturn ?? EmptyDashboard());
        }

        public Task<Contracts.DashboardRevision> GetDashboardRevisionAsync(
            string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(Contracts.DashboardRevision.Empty);
    }
}
