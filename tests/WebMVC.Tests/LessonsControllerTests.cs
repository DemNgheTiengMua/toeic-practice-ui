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

public sealed class LessonsControllerTests
{
    private static LessonsController CreateController(TestApiClient api, ClaimsPrincipal? principal = null)
    {
        principal ??= new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, Roles.Learner)
        ], "test"));

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        var props = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "mock.jwt.token" });
        var authServiceMock = new FixedAuthentication(principal, props);
        services.AddSingleton<IAuthenticationService>(authServiceMock);
        services.AddSingleton<ILessonClient>(api);
        services.AddSingleton<ILearnerContextClient>(api);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new LessonsController(api, currentUser, NullLogger<LessonsController>.Instance, api)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider())
        };
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestApiClient : ILessonClient, ILearnerContextClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public LessonDetailDto? Lesson { get; set; }
        public LessonProgressDto? Progress { get; set; }
        public int? LastRecordedLessonId { get; set; }
        public int? LastRecordedCardId { get; set; }

        public Task<LessonDetailDto> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<LessonDetailDto>(ExceptionToThrow) : Task.FromResult(Lesson!);

        public Task<LessonProgressDto> GetLessonProgressAsync(int lessonId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<LessonProgressDto>(ExceptionToThrow) : Task.FromResult(Progress!);

        public Task RecordCardViewAsync(int lessonId, int cardId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException(ExceptionToThrow);
            LastRecordedLessonId = lessonId;
            LastRecordedCardId = cardId;
            return Task.CompletedTask;
        }

        public Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<LessonDetailDto?>(ExceptionToThrow) : Task.FromResult(Lesson);

        public MicroCheckResultDto? MicroCheckResult { get; set; }
        public string? LastSelectedOption { get; set; }

        public Task<MicroCheckResultDto> SubmitMicroCheckAsync(int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException<MicroCheckResultDto>(ExceptionToThrow);
            LastSelectedOption = selectedOption;
            return Task.FromResult(MicroCheckResult!);
        }

        public Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [], 10));

        public Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Index_WhenLessonExists_ReturnsViewWithModel()
    {
        var lesson = new LessonDetailDto(
            LessonId: 10,
            Title: "Present Continuous",
            Summary: "Summary",
            SkillId: 1,
            CategoryId: 5,
            EstimatedMinutes: 5,
            Cards: []
        );
        var progress = new LessonProgressDto(
            CardsCompleted: 1,
            TotalCards: 4,
            IsCompleted: false,
            LastViewedCardId: null,
            FirstOpenedAt: null,
            CompletedAt: null,
            UpdatedAt: null
        );

        var api = new TestApiClient { Lesson = lesson, Progress = progress };
        var controller = CreateController(api);

        var result = await controller.Index(10, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LessonViewModel>(viewResult.Model);
        Assert.Equal(10, model.Lesson.LessonId);
        Assert.Equal(1, model.Progress.CardsCompleted);
        // #232: the reader is addressed by the lesson alone; there is no module to carry.
        Assert.Equal("/learning-path", model.FinishUrl);
    }

    [Fact]
    public async Task Index_WhenNotFound_RedirectsToLearningPathWithShellError()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.LessonNotFound, "Lesson not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.Index(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("LearningPath", redirect.ControllerName);
        Assert.Equal("Lesson not found or unavailable.", controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task ByCategory_WhenLessonFound_RedirectsToLesson()
    {
        var lesson = new LessonDetailDto(
            LessonId: 42,
            Title: "Grammar lesson",
            Summary: "Summary",
            SkillId: 1,
            CategoryId: 15,
            EstimatedMinutes: 5,
            Cards: []
        );
        var api = new TestApiClient { Lesson = lesson };
        var controller = CreateController(api);

        var result = await controller.ByCategory(15, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues!["id"]);
        // #232: there is no module to carry in the URL. A step is a coordinate, so the lesson reader
        // is addressed by the lesson alone.
        Assert.False(redirect.RouteValues!.ContainsKey("moduleId"));
    }

    [Fact]
    public async Task ByCategory_WhenLessonNotFound_RedirectsToLearningPath()
    {
        var api = new TestApiClient { Lesson = null };
        var controller = CreateController(api);

        var result = await controller.ByCategory(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("LearningPath", redirect.ControllerName);
        Assert.Equal("No lesson found for this topic yet.", controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task RecordCardView_WhenSuccessful_ReturnsJsonSuccess()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.RecordCardView(10, 2, CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(10, api.LastRecordedLessonId);
        Assert.Equal(2, api.LastRecordedCardId);
    }

    // #155: the micro-check goes to the API through MVC - never graded in the browser - and the reply
    // is what tells the page the right answer.
    [Fact]
    public async Task SubmitMicroCheck_ForwardsTheSelectedOptionAndReturnsTheGrade()
    {
        var api = new TestApiClient { MicroCheckResult = new MicroCheckResultDto(true, "B", "Because.", 5) };
        var controller = CreateController(api);

        var result = await controller.SubmitMicroCheck(10, 22, "B", CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal("B", api.LastSelectedOption);
        var payload = json.Value!;
        Assert.True((bool)payload.GetType().GetProperty("isCorrect")!.GetValue(payload)!);
        Assert.Equal("B", payload.GetType().GetProperty("correctOption")!.GetValue(payload));
    }
}
