using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class WritingControllerTests
{
    [Fact]
    public async Task Instructions_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            WritingInstructions = new WritingInstructionsDto(
                1, 1, "TOEIC", "TOEIC Writing Practice", "Test description", 60, 2, 1, 5, null,
                [new WritingSectionInfoDto(1, "W1", "Sentence", 1, null, 1)])
        };

        var controller = CreateController(api);
        var result = await controller.Instructions(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WritingInstructionsViewModel>(viewResult.Model);
        Assert.Equal("TOEIC Writing Practice", model.Instructions.ExamName);
        Assert.Equal(5, model.Instructions.CreditBalance);
    }

    [Fact]
    public async Task Instructions_when_not_found_redirects_to_Practice_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.ExamNotFound, "Exam not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Instructions(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Practice", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Start_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            StartWritingResponse = new StartWritingAttemptResponse(
                100, 1, AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 3600, false)
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Take), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Start_when_insufficient_credits_redirects_to_Instructions_with_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "No credits", 402)
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Instructions), redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Take_when_attempt_active_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            TakeDto = new WritingTakeDto(
                100, 1, "TOEIC Writing", 1, "TOEIC", AttemptStatusDto.InProgress,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 3600, 10, 1, 1,
                new WritingQuestionDto(10, 1, "Describe picture", 5, 20, 480, null, "W1", "Sentence", null, null, "My response", null, FreeResponseStatusDto.Draft),
                [new WritingQuestionNavDto(10, 1, "W1", "Sentence", true, true)])
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WritingTakeViewModel>(viewResult.Model);
        Assert.Equal(100, model.Take.AttemptId);
        Assert.Equal("My response", model.Take.Question.ResponseText);
    }

    [Fact]
    public async Task Take_when_attempt_expired_redirects_to_Practice_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptExpired, "Attempt expired", 410)
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Practice", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Take_when_attempt_graded_redirects_to_Review()
    {
        var api = new TestApiClient
        {
            TakeDto = new WritingTakeDto(
                100, 1, "TOEIC Writing", 1, "TOEIC", AttemptStatusDto.Graded,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 0, 10, 1, 1,
                new WritingQuestionDto(10, 1, "Describe picture", null, null, null, null, "W1", "Sentence", null, null, null, null, FreeResponseStatusDto.Graded),
                [])
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Review), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Take_when_attempt_submitted_redirects_to_Pending()
    {
        var api = new TestApiClient
        {
            TakeDto = new WritingTakeDto(
                100, 1, "TOEIC Writing", 1, "TOEIC", AttemptStatusDto.Submitted,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 3000, 10, 1, 1,
                new WritingQuestionDto(10, 1, "Describe picture", null, null, null, null, "W1", "Sentence", null, null, null, null, FreeResponseStatusDto.PendingAi),
                [])
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Pending), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task SaveDraft_returns_ok_with_result()
    {
        var api = new TestApiClient
        {
            SaveDraftResponse = new SaveWritingDraftResponse(50, 10, DateTimeOffset.UtcNow, 7)
        };

        var controller = CreateController(api);
        var result = await controller.SaveDraft(new SaveWritingDraftAjaxModel(100, 10, "Hello world"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var res = Assert.IsType<SaveWritingDraftResponse>(ok.Value);
        Assert.Equal(50, res.FreeResponseId);
        Assert.Equal(7, res.WordCount);
    }

    [Fact]
    public async Task Submit_redirects_to_Pending()
    {
        var api = new TestApiClient
        {
            SubmitResponse = new SubmitWritingResponse(100, AttemptStatusDto.Submitted, DateTimeOffset.UtcNow)
        };

        var controller = CreateController(api);
        var result = await controller.Submit(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Pending), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Pending_when_graded_redirects_to_Review()
    {
        var api = new TestApiClient
        {
            PendingDto = new WritingGradingPendingDto(
                100, 1, "TOEIC Writing", "TOEIC", AttemptStatusDto.Graded, 2, 2, 0, 0, false, null, [])
        };

        var controller = CreateController(api);
        var result = await controller.Pending(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Review), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Pending_when_still_pending_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            PendingDto = new WritingGradingPendingDto(
                100, 1, "TOEIC Writing", "TOEIC", AttemptStatusDto.Submitted, 2, 1, 0, 1, false, null, [])
        };

        var controller = CreateController(api);
        var result = await controller.Pending(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WritingPendingViewModel>(viewResult.Model);
        Assert.Equal(100, model.Pending.AttemptId);
        Assert.False(model.Pending.CanRetry);
    }

    [Fact]
    public async Task Retry_redirects_to_Pending()
    {
        var api = new TestApiClient();

        var controller = CreateController(api);
        var result = await controller.Retry(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WritingController.Pending), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Review_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            ReviewDto = new WritingReviewDto(
                100, 1, "TOEIC Writing", "TOEIC", DateTimeOffset.UtcNow, 2, 2, 12.0m, 16.0m,
                [new WritingQuestionReviewItemDto(10, 1, "Sentence", "Prompt", "My answer", 8, 6.0m, 6.0m,
                    [new WritingCriterionScoreItemDto(1, "GRAM", "Grammar", 3.0m, 3.0m, "Good grammar.")])])
        };

        var controller = CreateController(api);
        var result = await controller.Review(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WritingReviewViewModel>(viewResult.Model);
        Assert.Equal(100, model.Review.AttemptId);
        Assert.Equal(12.0m, model.Review.TotalScore);
        Assert.Equal(16.0m, model.Review.MaxScore);
    }

    private static WritingController CreateController(TestApiClient api)
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

        var controller = new WritingController(api, currentUser, NullLogger<WritingController>.Instance)
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
        public WritingInstructionsDto? WritingInstructions { get; set; }
        public StartWritingAttemptResponse? StartWritingResponse { get; set; }
        public WritingTakeDto? TakeDto { get; set; }
        public SaveWritingDraftResponse? SaveDraftResponse { get; set; }
        public SubmitWritingResponse? SubmitResponse { get; set; }
        public WritingGradingPendingDto? PendingDto { get; set; }
        public WritingReviewDto? ReviewDto { get; set; }

        public Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WritingInstructionsDto>(ExceptionToThrow) : Task.FromResult(WritingInstructions!);

        public Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartWritingAttemptResponse>(ExceptionToThrow) : Task.FromResult(StartWritingResponse!);

        public Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WritingTakeDto>(ExceptionToThrow) : Task.FromResult(TakeDto!);

        public Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveWritingDraftResponse>(ExceptionToThrow) : Task.FromResult(SaveDraftResponse!);

        public Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SubmitWritingResponse>(ExceptionToThrow) : Task.FromResult(SubmitResponse!);

        public Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WritingGradingPendingDto>(ExceptionToThrow) : Task.FromResult(PendingDto!);

        public Task RetryWritingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;

        public Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WritingReviewDto>(ExceptionToThrow) : Task.FromResult(ReviewDto!);
    }
}
