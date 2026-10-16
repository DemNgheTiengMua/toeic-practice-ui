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

public sealed class WeaknessAnalysisControllerTests
{
    private static WeaknessAnalysisDto SampleDto(bool hasData = true) => new(
        new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
        HasData: hasData,
        GradedExamCount: hasData ? 2 : 0,
        CompletedPracticeCount: hasData ? 5 : 0,
        TotalAnswered: hasData ? 40 : 0,
        TotalCorrect: hasData ? 28 : 0,
        OverallAccuracyPercentage: hasData ? 70 : 0,
        Sections: hasData
            ? [
                new SectionAccuracyDto(1, "P7", "Reading Comprehension", 20, 10, 50, IsWeakest: true, IsStrongest: false),
                new SectionAccuracyDto(2, "P1", "Photographs", 20, 18, 90, IsWeakest: false, IsStrongest: true)
              ]
            : [
                new SectionAccuracyDto(1, "P7", "Reading Comprehension", 0, 0, 0, IsWeakest: false, IsStrongest: false)
              ],
        FrequentlyMissedCategories: hasData
            ? [
                new CategoryAccuracyDto(1, "inference", "Inference questions", 10, 4, 40)
              ]
            : [],
        Recommendations: hasData
            ? [
                new RecommendationItemDto(101, "Focus on inference questions in Part 7", RecommendationSeverityDto.Major, "B2", "Reading", DateTimeOffset.UtcNow, false)
              ]
            : [],
        PriorityPracticeSection: hasData
            ? new SectionAccuracyDto(1, "P7", "Reading Comprehension", 20, 10, 50, IsWeakest: true, IsStrongest: false)
            : null);

    [Fact]
    public async Task Index_returns_view_with_weakness_analysis_model()
    {
        var api = new TestApiClient
        {
            WeaknessData = SampleDto(true),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WeaknessAnalysisViewModel>(viewResult.Model);
        Assert.True(model.Data.HasData);
        Assert.Equal("TOEIC", model.Data.Certificate.Code);
        Assert.Equal(2, model.Data.Sections.Count);
        Assert.Single(model.Data.Recommendations);
        Assert.NotNull(model.Data.PriorityPracticeSection);
    }

    [Fact]
    public async Task Index_prefetches_dashboard_insight_into_viewbag()
    {
        var sampleInsight = new AiInsightDto(
            10, 7, 1, AiInsightScopeDto.Dashboard, null, null,
            "Diagnostic overview", AiInsightTrendDto.Improving, AiInsightStatusDto.Done,
            null, "fake-ai-insight-v1", 160, 130, "fingerprint",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        var api = new TestApiClient
        {
            WeaknessData = SampleDto(true),
            InsightData = sampleInsight
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var insightInViewBag = Assert.IsType<AiInsightDto>(viewResult.ViewData["Insight"]);
        Assert.Equal(10, insightInViewBag.InsightId);
        Assert.Equal("Diagnostic overview", insightInViewBag.Narrative);
    }

    [Fact]
    public async Task Index_succeeds_even_when_dashboard_insight_prefetch_fails()
    {
        var api = new TestApiClient
        {
            WeaknessData = SampleDto(true),
            InsightData = null // Throws exception in TestApiClient
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WeaknessAnalysisViewModel>(viewResult.Model);
        Assert.True(model.Data.HasData);
        Assert.Null(viewResult.ViewData["Insight"]);
    }

    [Fact]
    public async Task Index_renders_empty_state_model_when_has_no_data()
    {
        var api = new TestApiClient
        {
            WeaknessData = SampleDto(false),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WeaknessAnalysisViewModel>(viewResult.Model);
        Assert.False(model.Data.HasData);
        Assert.Equal(0, model.Data.TotalAnswered);
        Assert.Empty(model.Data.Recommendations);
        Assert.Null(model.Data.PriorityPracticeSection);
    }

    [Fact]
    public async Task Index_redirects_to_dashboard_on_api_failure()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Unexpected, "Error", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task ResolveRecommendation_returns_json_when_ajax_request()
    {
        var api = new TestApiClient
        {
            ResolveResult = new ResolveRecommendationResultDto(101, true),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.ResolveRecommendation(101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("success").GetBoolean());
        Assert.Equal(101, val.GetProperty("recId").GetInt32());
        Assert.True(val.GetProperty("isResolved").GetBoolean());
    }

    [Fact]
    public async Task ResolveRecommendation_redirects_when_standard_post()
    {
        var api = new TestApiClient
        {
            ResolveResult = new ResolveRecommendationResultDto(101, true),
        };

        var controller = CreateController(api);
        var result = await controller.ResolveRecommendation(101, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task ResolveRecommendation_handles_api_exception_on_ajax()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.RecommendationNotFound, "Not found", 404, "Recommendation not found"),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.ResolveRecommendation(999, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    private static WeaknessAnalysisController CreateController(TestApiClient api)
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

        var controller = new WeaknessAnalysisController(api, currentUser, NullLogger<WeaknessAnalysisController>.Instance)
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
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestApiClient : IWeaknessAnalysisClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public WeaknessAnalysisDto? WeaknessData { get; set; }
        public ResolveRecommendationResultDto? ResolveResult { get; set; }
        public AiInsightDto? InsightData { get; set; }

        public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
            InsightData != null ? Task.FromResult(InsightData) : Task.FromException<AiInsightDto>(new Exception("No insight"));

        public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WeaknessAnalysisDto>(ExceptionToThrow) : Task.FromResult(WeaknessData!);

        public Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<ResolveRecommendationResultDto>(ExceptionToThrow) : Task.FromResult(ResolveResult!);
    }
}
