using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class DashboardControllerTests
{
    private static LearnerDashboardDto SampleDashboard() => new(
        new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
        LatestResult: new LatestResultDto(750, "B2", "Vantage", DateTimeOffset.UtcNow),
        IsNew: false,
        AttemptCount: 3,
        PracticeCount: 5,
        RecentAttempts: [],
        RecentPractice: [],
        UnfinishedAttempt: null,
        UnfinishedPractice: null
    );

    private static AiInsightDto SampleInsight() => new(
        InsightId: 10,
        UserId: 7,
        CertId: 1,
        Scope: AiInsightScopeDto.Dashboard,
        PracticeSessionId: null,
        AttemptId: null,
        Narrative: "Performance Summary: Great overall accuracy.",
        Trend: AiInsightTrendDto.Improving,
        Status: AiInsightStatusDto.Done,
        FailureReason: null,
        Model: "fake-ai-insight-v1",
        PromptTokens: 160,
        CompletionTokens: 130,
        SourceFingerprint: "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
        CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
        CompletedAt: DateTimeOffset.UtcNow,
        Recommendations:
        [
            new RecommendationDto(1, 10, null, null, "Review section rules", RecommendationSeverityDto.Major, "B2", "Reading", false, DateTimeOffset.UtcNow)
        ]
    );

    [Fact]
    public async Task Index_prefetches_insight_into_viewbag()
    {
        var sampleDashboard = SampleDashboard();
        var sampleInsight = SampleInsight();

        var api = new TestApiClient
        {
            DashboardData = sampleDashboard,
            InsightData = sampleInsight,
            MeData = new AuthUserDto(7, "learner@test.local", "Learner", Roles.Learner, true)
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Equal("TOEIC", model.Dashboard.Certificate.Code);

        var insightInViewBag = Assert.IsType<AiInsightDto>(viewResult.ViewData["Insight"]);
        Assert.Equal(10, insightInViewBag.InsightId);
        Assert.Equal(AiInsightScopeDto.Dashboard, insightInViewBag.Scope);
        Assert.Equal("Performance Summary: Great overall accuracy.", insightInViewBag.Narrative);
        Assert.Equal("<p>Performance Summary: Great overall accuracy.</p>", insightInViewBag.FormattedNarrative);
        Assert.Equal(AiInsightTrendDto.Improving, insightInViewBag.Trend);
    }

    [Fact]
    public async Task Index_succeeds_even_when_insight_prefetch_fails()
    {
        var sampleDashboard = SampleDashboard();

        var api = new TestApiClient
        {
            DashboardData = sampleDashboard,
            InsightExceptionToThrow = new ApiException("insight_error", "Insight server error", 500, null),
            MeData = new AuthUserDto(7, "learner@test.local", "Learner", Roles.Learner, true)
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Equal("TOEIC", model.Dashboard.Certificate.Code);
        Assert.Null(viewResult.ViewData["Insight"]);
    }

    [Fact]
    public async Task GetDashboardInsight_returns_ok_with_dto()
    {
        var sampleInsight = SampleInsight() with
        {
            Narrative = "**Performance Summary**: Great *overall* accuracy.\n\n- Drill `Part 5` daily\n<script>alert(1)</script>"
        };
        var api = new TestApiClient
        {
            InsightData = sampleInsight
        };

        var controller = CreateController(api);
        var result = await controller.GetDashboardInsight(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<AiInsightDto>(okResult.Value);
        Assert.Equal(10, returned.InsightId);
        Assert.Equal(AiInsightScopeDto.Dashboard, returned.Scope);
        Assert.Equal(AiInsightStatusDto.Done, returned.Status);
        Assert.NotNull(returned.FormattedNarrative);
        Assert.Contains("<strong>Performance Summary</strong>", returned.FormattedNarrative);
        Assert.Contains("<em>overall</em>", returned.FormattedNarrative);
        Assert.Contains("<code>Part 5</code>", returned.FormattedNarrative);
        Assert.DoesNotContain("<script>", returned.FormattedNarrative);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", returned.FormattedNarrative);
    }

    [Fact]
    public async Task RetryDashboardInsight_calls_api_and_returns_ok()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.RetryDashboardInsight(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(api.RetryDashboardInsightCalled);
        Assert.Equal(1, api.RetryDashboardInsightCertId);
    }

    [Fact]
    public async Task GetDashboardInsight_maps_api_exception_to_status_code()
    {
        var api = new TestApiClient
        {
            InsightExceptionToThrow = new ApiException("certificate_unavailable", "Certificate unavailable", 404, null)
        };

        var controller = CreateController(api);
        var result = await controller.GetDashboardInsight(1, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public async Task RetryDashboardInsight_maps_api_exception_to_status_code()
    {
        var api = new TestApiClient
        {
            RetryExceptionToThrow = new ApiException("insight_not_found", "Not found", 404, null)
        };

        var controller = CreateController(api);
        var result = await controller.RetryDashboardInsight(1, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    private static DashboardController CreateController(TestApiClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var controller = new DashboardController(api, api, currentUser, NullLogger<DashboardController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };

        return controller;
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext? context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext? context, IDictionary<string, object>? values) { }
    }

    private sealed class TestApiClient : ILearnerContextClient, IAuthClient
    {
        public LearnerDashboardDto? DashboardData { get; set; }
        public AuthUserDto? MeData { get; set; }
        public AiInsightDto? InsightData { get; set; }
        public Exception? InsightExceptionToThrow { get; set; }
        public Exception? RetryExceptionToThrow { get; set; }
        public bool RetryDashboardInsightCalled { get; set; }
        public int? RetryDashboardInsightCertId { get; set; }

        public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardData!);

        public Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MeData!);

        public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
            InsightExceptionToThrow != null ? Task.FromException<AiInsightDto>(InsightExceptionToThrow) : Task.FromResult(InsightData!);

        public Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default)
        {
            if (RetryExceptionToThrow != null) throw RetryExceptionToThrow;
            RetryDashboardInsightCalled = true;
            RetryDashboardInsightCertId = certId;
            return Task.CompletedTask;
        }

        public Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [], 8));

        public Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthTokenResponse>(null!);
        public Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthTokenResponse>(null!);
        public Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResendActivationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
