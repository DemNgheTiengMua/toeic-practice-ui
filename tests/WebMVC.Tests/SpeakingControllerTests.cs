using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
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

public sealed class SpeakingControllerTests
{
    [Fact]
    public async Task Instructions_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            SpeakingInstructions = new SpeakingInstructionsDto(
                1, 1, "TOEIC", "TOEIC Speaking Practice", "Test description", 20, 2, 1, 5, null,
                [new SpeakingSectionInfoDto(1, "S1", "Read aloud", 1, 45, 45, 1)])
        };

        var controller = CreateController(api);
        var result = await controller.Instructions(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SpeakingInstructionsViewModel>(viewResult.Model);
        Assert.Equal("TOEIC Speaking Practice", model.Instructions.ExamName);
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
            StartSpeakingResponse = new StartSpeakingAttemptResponse(
                100, 1, AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(20), 1200, false)
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SpeakingController.Take), redirect.ActionName);
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
        Assert.Equal(nameof(SpeakingController.Instructions), redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Take_returns_view_with_model()
    {
        var takeDto = CreateSampleTakeDto(AttemptStatusDto.InProgress);
        var api = new TestApiClient { TakeDto = takeDto };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SpeakingTakeViewModel>(viewResult.Model);
        Assert.Equal(100, model.Take.AttemptId);
        Assert.Equal("Read aloud", model.Take.Question.Stem);
    }

    [Fact]
    public async Task Take_when_already_submitted_redirects_to_Pending()
    {
        var takeDto = CreateSampleTakeDto(AttemptStatusDto.Submitted);
        var api = new TestApiClient { TakeDto = takeDto };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SpeakingController.Pending), redirect.ActionName);
    }

    [Fact]
    public async Task Take_when_graded_redirects_to_Review()
    {
        var takeDto = CreateSampleTakeDto(AttemptStatusDto.Graded);
        var api = new TestApiClient { TakeDto = takeDto };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SpeakingController.Review), redirect.ActionName);
    }

    [Fact]
    public async Task UploadAudio_when_empty_returns_bad_request()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.UploadAudio(100, 1, null, 1000, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task UploadAudio_forwards_to_api_and_returns_ok()
    {
        var api = new TestApiClient
        {
            UploadResponse = new UploadSpeakingAudioResponse(10, 1, "attempts/100/q1.webm", 25000, DateTimeOffset.UtcNow)
        };
        var controller = CreateController(api);

        var fakeFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("fake audio")), 0, 10, "audioFile", "test.webm")
        {
            Headers = new HeaderDictionary(),
            ContentType = "audio/webm"
        };

        var result = await controller.UploadAudio(100, 1, fakeFile, 25000, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UploadSpeakingAudioResponse>(okResult.Value);
        Assert.Equal("attempts/100/q1.webm", response.AudioPath);
    }

    [Fact]
    public async Task Submit_redirects_to_Pending()
    {
        var api = new TestApiClient
        {
            SubmitResponse = new SubmitSpeakingResponse(100, AttemptStatusDto.Submitted, DateTimeOffset.UtcNow)
        };

        var controller = CreateController(api);
        var result = await controller.Submit(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SpeakingController.Pending), redirect.ActionName);
    }

    [Fact]
    public async Task Pending_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            PendingDto = new SpeakingGradingPendingDto(
                100, 1, "TOEIC Speaking", "TOEIC", AttemptStatusDto.Submitted, 2, 0, 0, 2, false, null, [])
        };

        var controller = CreateController(api);
        var result = await controller.Pending(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SpeakingPendingViewModel>(viewResult.Model);
        Assert.Equal(100, model.Pending.AttemptId);
    }

    [Fact]
    public async Task Status_returns_json()
    {
        var api = new TestApiClient
        {
            PendingDto = new SpeakingGradingPendingDto(
                100, 1, "TOEIC Speaking", "TOEIC", AttemptStatusDto.Submitted, 2, 1, 0, 1, false, null, [])
        };

        var controller = CreateController(api);
        var result = await controller.Status(100, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<SpeakingGradingPendingDto>(okResult.Value);
        Assert.Equal(1, data.GradedResponses);
    }

    [Fact]
    public async Task Retry_redirects_to_Pending()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        var result = await controller.Retry(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SpeakingController.Pending), redirect.ActionName);
    }

    [Fact]
    public async Task Review_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            ReviewDto = new SpeakingReviewDto(
                100, 1, "TOEIC Speaking", "TOEIC", DateTimeOffset.UtcNow, 2, 2, 12.0m, 16.0m, 150, [], null)
        };

        var controller = CreateController(api);
        var result = await controller.Review(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SpeakingReviewViewModel>(viewResult.Model);
        Assert.Equal(150, model.Review.ScaledScore);
    }

    [Fact]
    public async Task Audio_when_allowed_returns_file_result()
    {
        var audioStream = new MemoryStream(Encoding.UTF8.GetBytes("sample audio bytes"));
        var api = new TestApiClient
        {
            AudioStreamResult = (audioStream, "audio/webm", "q1.webm")
        };

        var controller = CreateController(api);
        var result = await controller.Audio(100, 1, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/webm", fileResult.ContentType);
        Assert.Equal("q1.webm", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task Audio_when_forbidden_returns_forbid()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Forbidden, "Forbidden", 403)
        };

        var controller = CreateController(api);
        var result = await controller.Audio(100, 1, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    private static SpeakingTakeDto CreateSampleTakeDto(AttemptStatusDto status)
    {
        var q = new SpeakingQuestionDto(
            1, 1, "Read aloud", 45, 45, null, "S1", "Read aloud", null, null, null, null, null, null, null, FreeResponseStatusDto.Draft);

        return new SpeakingTakeDto(
            100, 1, "TOEIC Speaking", 1, "TOEIC", status,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(20), 1200, 1, 1, 2, 0,
            q, [new SpeakingQuestionNavDto(1, 1, "S1", "Read aloud", false, true)]);
    }

    private static SpeakingController CreateController(TestApiClient api, int userId = 42, string role = "learner")
    {
        var identity = new ClaimsIdentity(AuthClaims.For(userId, "learner@test.local", role, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });
        var controller = new SpeakingController(api, currentUser, NullLogger<SpeakingController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EphemeralTempDataProvider())
        };

        return controller;
    }

    private sealed class EphemeralTempDataProvider : ITempDataProvider
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
        public SpeakingInstructionsDto? SpeakingInstructions { get; set; }
        public StartSpeakingAttemptResponse? StartSpeakingResponse { get; set; }
        public SpeakingTakeDto? TakeDto { get; set; }
        public UploadSpeakingAudioResponse? UploadResponse { get; set; }
        public SubmitSpeakingResponse? SubmitResponse { get; set; }
        public SpeakingGradingPendingDto? PendingDto { get; set; }
        public SpeakingReviewDto? ReviewDto { get; set; }
        public (Stream Stream, string ContentType, string FileName)? AudioStreamResult { get; set; }

        public Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SpeakingInstructionsDto>(ExceptionToThrow) : Task.FromResult(SpeakingInstructions!);

        public Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartSpeakingAttemptResponse>(ExceptionToThrow) : Task.FromResult(StartSpeakingResponse!);

        public Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SpeakingTakeDto>(ExceptionToThrow) : Task.FromResult(TakeDto!);

        public Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<UploadSpeakingAudioResponse>(ExceptionToThrow) : Task.FromResult(UploadResponse!);

        public Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SubmitSpeakingResponse>(ExceptionToThrow) : Task.FromResult(SubmitResponse!);

        public Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SpeakingGradingPendingDto>(ExceptionToThrow) : Task.FromResult(PendingDto!);

        public Task RetrySpeakingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;

        public Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SpeakingReviewDto>(ExceptionToThrow) : Task.FromResult(ReviewDto!);

        public Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(int attemptId, int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<(Stream Stream, string ContentType, string FileName)>(ExceptionToThrow) : Task.FromResult(AudioStreamResult!.Value);
    }
}
