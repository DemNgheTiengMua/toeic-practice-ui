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
            : null,
        Topics: hasData
            ? [
                // #243: one topic at two levels, two lessons - so "the cell's lesson" and "the
                // topic's first lesson" are different answers and the test can tell them apart.
                new TopicAccuracyDto(4, "R-DET", "Local detail", ModalityDto.Reading, "B1", 14, 6, 43,
                    IsWeakest: true, ObservationFloor: 12, LessonId: 41),
                new TopicAccuracyDto(4, "R-DET", "Local detail", ModalityDto.Reading, "B2", 2, 0, 0,
                    IsWeakest: false, ObservationFloor: 12, LessonId: 47)
              ]
            : [],
        WeakestTopic: hasData
            ? new TopicAccuracyDto(4, "R-DET", "Local detail", ModalityDto.Reading, "B1", 14, 6, 43,
                IsWeakest: true, ObservationFloor: 12, LessonId: 41)
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

    /// <summary>
    /// #243 criterion 4: the page's rows carry the lessons for <b>their own cells</b>.
    /// <para>
    /// <b>The wrong input this catches:</b> a fixture where the weakness is the only cell with a lesson,
    /// so "the topic's lesson" and "the cell's lesson" are the same id and either reading passes. The
    /// fixture here has two cells of one topic at two levels with different lesson ids, and the
    /// unranked cell must keep its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Index_links_each_cell_to_its_own_lesson()
    {
        var api = new TestApiClient { WeaknessData = SampleDto(true) };
        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var model = Assert.IsType<WeaknessAnalysisViewModel>(Assert.IsType<ViewResult>(result).Model);

        Assert.Equal(2, model.TopicRows.Count);
        var weakest = Assert.Single(model.TopicRows, r => r.IsWeakest);
        Assert.Equal("/lessons/41", weakest.LessonHref);
        Assert.Equal("B1", weakest.LevelLabel);

        // The other cell carries ITS OWN lesson, not the weakest cell's: a link built from the topic
        // alone would give both cells 41.
        var other = Assert.Single(model.TopicRows, r => !r.IsWeakest);
        Assert.Equal("/lessons/47", other.LessonHref);
        Assert.Equal("B2", other.LevelLabel);

        Assert.NotNull(model.WeakestTopic);
        Assert.Equal(41, int.Parse(model.WeakestTopic!.LessonHref!["/lessons/".Length..]));
    }

    /// <summary>
    /// #243 criterion 2 in the view model: a cell below the floor says so, and the page has no weakest
    /// topic to show. <b>The wrong input this catches:</b> a row model that renders "0% correct" for
    /// two answers and lets the template treat it as a ranked weakness.
    /// </summary>
    [Fact]
    public async Task Index_prepares_an_unranked_cell_with_its_reason()
    {
        var api = new TestApiClient { WeaknessData = SampleDto(true) };
        var result = await CreateController(api).Index(CancellationToken.None);

        var model = Assert.IsType<WeaknessAnalysisViewModel>(Assert.IsType<ViewResult>(result).Model);

        var unranked = Assert.Single(model.TopicRows, r => !r.HasEnoughEvidence);
        Assert.Contains("12", unranked.EvidenceNote, StringComparison.Ordinal);
        Assert.Contains("2 of the 12", unranked.EvidenceNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_prefetches_dashboard_insight_into_display_model()
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
        var insightInViewBag = Assert.IsType<AiInsightDto>(Assert.IsType<WeaknessAnalysisViewModel>(viewResult.Model).InsightPanel.Insight);
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
        Assert.Null(Assert.IsType<WeaknessAnalysisViewModel>(viewResult.Model).InsightPanel.Insight);
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
