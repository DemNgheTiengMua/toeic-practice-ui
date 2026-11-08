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
        new NextActionDto(
            "WEAKEST TOPIC",
            "Drill Incomplete Sentences",
            "58% accuracy over 12 answers in Reading.",
            "Start practice",
            "/practice",
            "warning",
            "free_drill",
            "READ · 58%"),
        new GoalStandingDto(
            CurrentBandCode: "B1",
            CurrentBandName: "Intermediate",
            CurrentScore: 750,
            CurrentSource: "mock",
            TargetBandCode: "B2",
            TargetBandName: "Vantage",
            TargetMinScore: 785,
            TargetDate: new DateOnly(2026, 12, 20),
            WeeklyStudyHours: 6m,
            Status: "stretch",
            CompletedPathSteps: 2,
            TotalPathSteps: 5,
            CurrentWeekNumber: 2,
            WeekPlannedMinutes: 120,
            WeekCompletedMinutes: 45,
            // #241: the fixture is a PLACED learner, so the block list is all three. The stored
            // verdict and ΔH are what block 2 prints.
            RequiredTotalHours: 120,
            OverallLevelCode: "B1",
            LimitingSkillCode: "LIST",
            LimitingSkillName: "Listening",
            HasNoMeasurement: false),
        Skills:
        [
            new SkillCardDto(1, "LIST", "Listening", ModalityDto.Listening, 380, 495, []),
            new SkillCardDto(2, "READ", "Reading", ModalityDto.Reading, 370, 495,
                [new SkillCategoryDto("P5", "Incomplete Sentences", 58, 12)])
        ],
        AttemptCount: 3,
        PracticeCount: 5);

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
    public async Task Index_returns_the_three_blocks_to_the_view()
    {
        var api = new TestApiClient
        {
            DashboardData = SampleDashboard(),
            MeData = new AuthUserDto(7, "learner@test.local", "Learner", Roles.Learner, true)
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Equal("TOEIC", model.Dashboard.Certificate.Code);

        // #159: the hero carries one action, the goal block carries the standing, and one column per
        // skill. Nothing else reaches the page.
        Assert.Equal("/practice", model.Dashboard.NextAction.ActionUrl);
        Assert.Equal("B1", model.Dashboard.Goal.CurrentBandCode);
        Assert.Equal(2, model.Dashboard.Skills.Count);
        Assert.False(model.ShowVerificationReminder);

        // #241: the block list is the page's shape, and this fixture is a placed learner, so it is
        // all three. A fourth block fails here before it fails in a browser.
        Assert.Equal(DashboardBlocks.Placed, model.Blocks);
        Assert.Equal(3, model.Blocks.Count);
        // Block 3's column count follows the certificate: two skills here.
        Assert.Equal(2, model.Skills.ColumnCount);    }

    [Fact]
    public async Task Index_still_renders_when_the_verification_status_cannot_be_read()
    {
        var api = new TestApiClient
        {
            DashboardData = SampleDashboard(),
            MeExceptionToThrow = new HttpRequestException("API unreachable")
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("TOEIC", model.Dashboard.Certificate.Code);
        // Unknown, not "unverified": a failed read must not tell the learner to verify an address
        // that may already be confirmed.
        Assert.False(model.ShowVerificationReminder);
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
        public Exception? MeExceptionToThrow { get; set; }
        public AiInsightDto? InsightData { get; set; }
        public Exception? InsightExceptionToThrow { get; set; }
        public Exception? RetryExceptionToThrow { get; set; }
        public bool RetryDashboardInsightCalled { get; set; }
        public int? RetryDashboardInsightCertId { get; set; }

        public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardData!);

        public Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default) =>
            MeExceptionToThrow != null ? Task.FromException<AuthUserDto>(MeExceptionToThrow) : Task.FromResult(MeData!);

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
