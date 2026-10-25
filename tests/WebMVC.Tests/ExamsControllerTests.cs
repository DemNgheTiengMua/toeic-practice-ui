using System.Security.Claims;
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

namespace WebMVC.Tests;

public sealed class ExamsControllerTests
{
    [Fact]
    public async Task Index_returns_view_with_catalog()
    {
        var api = new TestApiClient
        {
            MockExamsCatalog = new MockExamListViewDto(
                new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
                8,
                [
                    new MockExamItemDto(1, 1, "TOEIC", "ETS 2024 — Test 1", "Full test", 120, 200, "ETS", 1, null, null, 750, "B1")
                ]),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamIndexViewModel>(viewResult.Model);
        Assert.Single(model.Catalog.Exams);
        Assert.Equal(8, model.Catalog.CreditBalance);
    }

    [Fact]
    public async Task Instructions_returns_view_with_instructions()
    {
        var api = new TestApiClient
        {
            Instructions = new MockExamInstructionsDto(
                1, 1, "TOEIC", "ETS 2024 — Test 1", "Full test", 120, 200, 1, 8, null,
                [
                    new MockExamSectionInfoDto(1, "listening", "Listening", ModalityDto.Listening, "P1", "Part 1", 6, 6, 45, 1)
                ]),
        };

        var controller = CreateController(api);
        var result = await controller.Instructions(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamInstructionsViewModel>(viewResult.Model);
        Assert.Equal("ETS 2024 — Test 1", model.Instructions.ExamName);
    }

    [Fact]
    public async Task Instructions_when_not_found_redirects_to_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.ExamNotFound, "Exam not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Instructions(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Start_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            StartResponse = new StartMockExamResponse(100, 1, AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200, false),
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Take), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Start_when_insufficient_credits_redirects_to_Instructions_with_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "You do not have enough credits", 402),
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Instructions), redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["id"]);
        Assert.Equal("You do not have enough credits to start this mock exam.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Take_with_active_attempt_returns_view()
    {
        var q = new AttemptQuestionDto(
            10, 1, 200, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
            "Look at the picture.", null, null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "Text A"), new AttemptOptionDto(2, "B", "Text B")],
            null, false);

        var take = new AttemptTakeDto(
            100, 1, "ETS 2024 — Test 1", "TOEIC", AttemptStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200,
            200, 0, 0, q,
            [new NavigatorItemDto(10, 1, 1, "P1", ModalityDto.Listening, false, false, true)],
            false);

        var api = new TestApiClient { TakeDto = take };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamTakeViewModel>(viewResult.Model);
        Assert.Equal(100, model.Take.AttemptId);
        Assert.Equal(10, model.Take.CurrentQuestion.QuestionId);
    }

    [Fact]
    public async Task Take_when_attempt_is_graded_redirects_to_Result()
    {
        var q = new AttemptQuestionDto(10, 1, 200, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, null, null, [], null, false);
        var take = new AttemptTakeDto(
            100, 1, "ETS 2024 — Test 1", "TOEIC", AttemptStatusDto.Graded,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 0,
            200, 200, 0, q, [], false);

        var api = new TestApiClient { TakeDto = take };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Result), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Take_when_attempt_is_expired_redirects_to_Result()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptExpired, "Attempt has expired", 400),
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Result), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Take_when_attempt_not_found_returns_404_view()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFound, "Attempt not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Take(999, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("AttemptNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task SaveAnswer_returns_json_result()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveAnswerResultDto(10, 2, true, 6000, false),
        };

        var controller = CreateController(api);
        var result = await controller.SaveAnswer(100, 10, new SaveAnswerFormModel(2, true), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var qIdProp = json.Value.GetType().GetProperty("questionId")?.GetValue(json.Value);
        var optIdProp = json.Value.GetType().GetProperty("selectedOptionId")?.GetValue(json.Value);
        var flagProp = json.Value.GetType().GetProperty("isFlagged")?.GetValue(json.Value);
        Assert.Equal(10, qIdProp);
        Assert.Equal(2, optIdProp);
        Assert.Equal(true, flagProp);
    }

    [Fact]
    public async Task SaveAnswer_when_expired_returns_json_with_auto_submitted()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptExpired, "Attempt expired", 400),
        };

        var controller = CreateController(api);
        var result = await controller.SaveAnswer(100, 10, new SaveAnswerFormModel(2, true), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var autoProp = json.Value.GetType().GetProperty("isAutoSubmitted")?.GetValue(json.Value);
        var redirProp = json.Value.GetType().GetProperty("redirectUrl")?.GetValue(json.Value);
        Assert.Equal(true, autoProp);
        Assert.NotNull(redirProp);
    }

    [Fact]
    public async Task SaveDraft_forwards_exam_draft_to_attempt_api()
    {
        var api = new TestApiClient();
        var request = new SaveAttemptDraftRequest(42, "Current writing response");

        var controller = CreateController(api);
        var result = await controller.SaveDraft(100, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(100, api.SavedDraftAttemptId);
        Assert.Equal(request, api.SavedDraftRequest);
    }

    [Fact]
    public async Task Submit_redirects_to_Result()
    {
        var api = new TestApiClient
        {
            SubmitResponse = new SubmitAttemptResponse(100, AttemptStatusDto.Graded, 800, "B2", "Independent user", DateTimeOffset.UtcNow, false),
        };

        var controller = CreateController(api);
        var result = await controller.Submit(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Result), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Submit_forwards_final_writing_drafts_with_the_attempt()
    {
        var api = new TestApiClient
        {
            SubmitResponse = new SubmitAttemptResponse(100, AttemptStatusDto.Graded, 800, "B2", "Independent user", DateTimeOffset.UtcNow, false),
        };
        var drafts = new List<SubmitAttemptDraftRequest>
        {
            new(42, "Latest editor content"),
        };

        var controller = CreateController(api);
        var result = await controller.Submit(100, CancellationToken.None, new ExamSubmitFormModel { Drafts = drafts });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(drafts, api.SubmittedDrafts);
    }

    [Fact]
    public async Task Cancel_redirects_to_Index()
    {
        var api = new TestApiClient();

        var controller = CreateController(api);
        var result = await controller.Cancel(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Index), redirect.ActionName);
        Assert.Equal("The exam attempt was cancelled.", controller.TempData["ShellSuccess"]);
    }

    [Fact]
    public async Task TransitionUnit_With_Body_Payload_Transitions_Unit_And_Returns_Json()
    {
        var api = new TestApiClient
        {
            TransitionResponse = new AttemptTransitionResponse(100, 1, 2, null, null, 1800, AttemptStatusDto.InProgress, false)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionUnit(100, new TransitionUnitRequest(2), null, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var transition = Assert.IsType<AttemptTransitionResponse>(json.Value);
        Assert.Equal(100, transition.AttemptId);
        Assert.Equal(2, transition.ActiveUnitNumber);
        Assert.NotNull(api.LastTransitionUnit);
        Assert.Equal(100, api.LastTransitionUnit.Value.attemptId);
        Assert.Equal(2, api.LastTransitionUnit.Value.request.TargetUnitNumber);
    }

    [Fact]
    public async Task TransitionUnit_With_Form_TargetUnitNumber_Transitions_Unit()
    {
        var api = new TestApiClient
        {
            TransitionResponse = new AttemptTransitionResponse(100, 1, 2, null, null, 1800, AttemptStatusDto.InProgress, false)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionUnit(100, null, 2, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var transition = Assert.IsType<AttemptTransitionResponse>(json.Value);
        Assert.Equal(2, transition.ActiveUnitNumber);
        Assert.NotNull(api.LastTransitionUnit);
        Assert.Equal(2, api.LastTransitionUnit.Value.request.TargetUnitNumber);
    }

    [Fact]
    public async Task TransitionUnit_When_Api_Throws_Returns_StatusCode_Error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("Cannot navigate back to a locked unit.", "WRONG_PHASE", 400)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionUnit(100, new TransitionUnitRequest(1), null, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task TransitionGroup_With_Body_Payload_Transitions_Group_And_Returns_Json()
    {
        var api = new TestApiClient
        {
            TransitionResponse = new AttemptTransitionResponse(100, 1, 1, 5, null, 600, AttemptStatusDto.InProgress, false)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionGroup(100, new TransitionGroupRequest(5), null, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var transition = Assert.IsType<AttemptTransitionResponse>(json.Value);
        Assert.Equal(100, transition.AttemptId);
        Assert.Equal(5, transition.ActiveGroupId);
        Assert.NotNull(api.LastTransitionGroup);
        Assert.Equal(100, api.LastTransitionGroup.Value.attemptId);
        Assert.Equal(5, api.LastTransitionGroup.Value.request.TargetGroupId);
    }

    [Fact]
    public async Task TransitionGroup_With_Form_TargetGroupId_Transitions_Group()
    {
        var api = new TestApiClient
        {
            TransitionResponse = new AttemptTransitionResponse(100, 1, 1, 6, null, 600, AttemptStatusDto.InProgress, false)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionGroup(100, null, 6, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var transition = Assert.IsType<AttemptTransitionResponse>(json.Value);
        Assert.Equal(6, transition.ActiveGroupId);
        Assert.NotNull(api.LastTransitionGroup);
        Assert.Equal(6, api.LastTransitionGroup.Value.request.TargetGroupId);
    }

    [Fact]
    public async Task TransitionGroup_When_Api_Throws_Returns_StatusCode_Error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("Question group belongs to a locked phase.", "WRONG_PHASE", 400)
        };
        var controller = CreateController(api);

        var result = await controller.TransitionGroup(100, new TransitionGroupRequest(2), null, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task Result_returns_view_with_result_data()
    {
        var resDto = new AttemptResultDto(
            100, 1, "ETS 2024 — Test 1", 1, "TOEIC", "TOEIC", AttemptStatusDto.Graded,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, 7200, false,
            750, "B1", "Threshold user",
            [new AttemptSkillScoreDto(1, "listening", "Listening", ModalityDto.Listening, 80, 400, 100)],
            700, 50);

        var api = new TestApiClient { ResultDto = resDto };

        var controller = CreateController(api);
        var result = await controller.Result(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResultViewModel>(viewResult.Model);
        Assert.Equal(750, model.Result.TotalScore);
        Assert.Equal(50, model.Result.ScoreDelta);
    }

    [Fact]
    public async Task GetInsight_returns_ok_with_insight_dto()
    {
        var sampleInsight = new AiInsightDto(
            1, 7, 1, AiInsightScopeDto.Attempt, null, 100,
            "Solid mock performance", AiInsightTrendDto.Improving, AiInsightStatusDto.Done,
            null, "fake-ai-insight-v1", 140, 110, "fingerprint",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        var api = new TestApiClient { InsightDto = sampleInsight };
        var controller = CreateController(api);

        var result = await controller.GetInsight(100, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<AiInsightDto>(okResult.Value);
        Assert.Equal(sampleInsight.InsightId, returned.InsightId);
        Assert.Equal(AiInsightStatusDto.Done, returned.Status);
    }

    [Fact]
    public async Task RetryInsight_calls_api_and_returns_ok()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.RetryInsight(100, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.True(api.RetryInsightCalled);
    }

    [Fact]
    public async Task Result_prefetches_insight_into_view_model()
    {
        var resDto = new AttemptResultDto(
            100, 1, "ETS 2024 — Test 1", 1, "TOEIC", "TOEIC", AttemptStatusDto.Graded,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, 7200, false,
            750, "B1", "Threshold user",
            [new AttemptSkillScoreDto(1, "listening", "Listening", ModalityDto.Listening, 80, 400, 100)],
            700, 50);

        var sampleInsight = new AiInsightDto(
            1, 7, 1, AiInsightScopeDto.Attempt, null, 100,
            "Pre-fetched exam insight", AiInsightTrendDto.Improving, AiInsightStatusDto.Done,
            null, "fake-ai-insight-v1", 140, 110, "fingerprint",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        var api = new TestApiClient { ResultDto = resDto, InsightDto = sampleInsight };
        var controller = CreateController(api);

        var result = await controller.Result(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResultViewModel>(viewResult.Model);
        Assert.NotNull(model.Insight);
        Assert.Equal("Pre-fetched exam insight", model.Insight.Narrative);
    }

    private static ExamsController CreateController(TestApiClient api, ISpeakingClient? speakingApi = null)
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

        var controller = new ExamsController(api, currentUser, NullLogger<ExamsController>.Instance, speakingApi)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
            Url = new TestUrlHelper(),
        };

        return controller;
    }

    private sealed class TestUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext => throw new NotImplementedException();
        public string? Action(UrlActionContext actionContext) => $"/exams/{actionContext.Action?.ToLower()}/{actionContext.Values?.GetType().GetProperty("attemptId")?.GetValue(actionContext.Values)}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }


    private sealed class TestSpeakingClient : ISpeakingClient
    {
        public UploadSpeakingAudioResponse? UploadResult { get; set; }
        public (Stream Stream, string ContentType, string FileName)? AudioStreamResult { get; set; }
        public int? UploadedAttemptId { get; set; }
        public int? UploadedQuestionId { get; set; }
        public string? UploadedFileName { get; set; }
        public int? UploadedDurationMs { get; set; }

        public Task<UploadSpeakingAudioResponse> UploadSpeakingAudioAsync(
            int attemptId, int questionId, Stream audioStream, string fileName, string contentType, int? durationMs, CancellationToken cancellationToken = default)
        {
            UploadedAttemptId = attemptId;
            UploadedQuestionId = questionId;
            UploadedFileName = fileName;
            UploadedDurationMs = durationMs;
            return Task.FromResult(UploadResult ?? new UploadSpeakingAudioResponse(1, questionId, "/audio/speaking-test.webm", durationMs, DateTimeOffset.UtcNow));
        }

        public Task<(Stream Stream, string ContentType, string FileName)> GetSpeakingAudioStreamAsync(
            int attemptId, int questionId, CancellationToken cancellationToken = default)
        {
            if (AudioStreamResult.HasValue) return Task.FromResult(AudioStreamResult.Value);
            return Task.FromResult((Stream: (Stream)new MemoryStream([1, 2, 3]), ContentType: "audio/webm", FileName: "test.webm"));
        }

        public Task<SpeakingInstructionsDto> GetSpeakingInstructionsAsync(int examId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<StartSpeakingAttemptResponse> StartSpeakingAttemptAsync(int examId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SpeakingTakeDto> GetSpeakingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SubmitSpeakingResponse> SubmitSpeakingAttemptAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SpeakingGradingPendingDto> GetSpeakingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RetrySpeakingGradingAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SpeakingReviewDto> GetSpeakingReviewAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class TestApiClient : IMockExamsClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public MockExamListViewDto? MockExamsCatalog { get; set; }
        public MockExamInstructionsDto? Instructions { get; set; }
        public StartMockExamResponse? StartResponse { get; set; }
        public AttemptTakeDto? TakeDto { get; set; }
        public SaveAnswerResultDto? SaveResult { get; set; }
        public SubmitAttemptResponse? SubmitResponse { get; set; }
        public IReadOnlyList<SubmitAttemptDraftRequest>? SubmittedDrafts { get; private set; }
        public int? SavedDraftAttemptId { get; private set; }
        public SaveAttemptDraftRequest? SavedDraftRequest { get; private set; }
        public AttemptResultDto? ResultDto { get; set; }
        public ExamHistoryDto? HistoryDto { get; set; }
        public AttemptReviewDto? ReviewDto { get; set; }
        public AiInsightDto? InsightDto { get; set; }
        public bool RetryInsightCalled { get; set; }
        public AttemptTransitionResponse? TransitionResponse { get; set; }

        public Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamListViewDto>(ExceptionToThrow) : Task.FromResult(MockExamsCatalog!);

        public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamInstructionsDto>(ExceptionToThrow) : Task.FromResult(Instructions!);

        public Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartMockExamResponse>(ExceptionToThrow) : Task.FromResult(StartResponse!);

        public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q = null, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTakeDto>(ExceptionToThrow) : Task.FromResult(TakeDto!);

        public Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveAnswerResultDto>(ExceptionToThrow) : Task.FromResult(SaveResult!);

        public Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, CancellationToken cancellationToken = default) =>
            SaveExamDraftAsync(attemptId, request);

        private Task<SaveAttemptDraftResponse> SaveExamDraftAsync(int attemptId, SaveAttemptDraftRequest request)
        {
            if (ExceptionToThrow != null) return Task.FromException<SaveAttemptDraftResponse>(ExceptionToThrow);
            SavedDraftAttemptId = attemptId;
            SavedDraftRequest = request;
            return Task.FromResult(new SaveAttemptDraftResponse(1, request.QuestionId, DateTimeOffset.UtcNow, 0));
        }

        public Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SubmitAttemptResponse>(ExceptionToThrow) : Task.FromResult(SubmitResponse!);

        public Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException<SubmitAttemptResponse>(ExceptionToThrow);
            SubmittedDrafts = request.Drafts;
            return Task.FromResult(SubmitResponse!);
        }

        public Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTransitionResponse>(ExceptionToThrow) : Task.FromResult(TransitionResponse ?? new AttemptTransitionResponse(attemptId, 1, 1, null, null, 1800, AttemptStatusDto.InProgress, false));

        public (int attemptId, TransitionUnitRequest request)? LastTransitionUnit { get; set; }
        public Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, CancellationToken cancellationToken = default)
        {
            LastTransitionUnit = (attemptId, request);
            return ExceptionToThrow != null
                ? Task.FromException<AttemptTransitionResponse>(ExceptionToThrow)
                : Task.FromResult(TransitionResponse ?? new AttemptTransitionResponse(attemptId, 1, request.TargetUnitNumber, null, null, 1800, AttemptStatusDto.InProgress, false));
        }

        public (int attemptId, TransitionGroupRequest request)? LastTransitionGroup { get; set; }
        public Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, CancellationToken cancellationToken = default)
        {
            LastTransitionGroup = (attemptId, request);
            return ExceptionToThrow != null
                ? Task.FromException<AttemptTransitionResponse>(ExceptionToThrow)
                : Task.FromResult(TransitionResponse ?? new AttemptTransitionResponse(attemptId, 1, 1, request.TargetGroupId, null, 600, AttemptStatusDto.InProgress, false));
        }

        public Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTransitionResponse>(ExceptionToThrow) : Task.FromResult(TransitionResponse ?? new AttemptTransitionResponse(attemptId, 1, 1, null, DateTimeOffset.UtcNow.AddSeconds(120), 120, AttemptStatusDto.InProgress, false));

        public Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;

        public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptResultDto>(ExceptionToThrow) : Task.FromResult(ResultDto!);

        public Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<ExamHistoryDto>(ExceptionToThrow) : Task.FromResult(HistoryDto!);

        public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptReviewDto>(ExceptionToThrow) : Task.FromResult(ReviewDto!);

        public Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AiInsightDto>(ExceptionToThrow) : Task.FromResult(InsightDto!);

        public Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException(ExceptionToThrow);
            RetryInsightCalled = true;
            return Task.CompletedTask;
        }

        public PreflightStatusDto? PreflightDto { get; set; }
        public bool VerifyPreflightCalled { get; set; }

        public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null
                ? Task.FromException<PreflightStatusDto>(ExceptionToThrow)
                : Task.FromResult(PreflightDto ?? new PreflightStatusDto(attemptId, 1, "Mock Exam", "TOEIC", "TOEIC", 120, false, false, false, true, DateTimeOffset.UtcNow, []));

        public Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException<PreflightStatusDto>(ExceptionToThrow);
            VerifyPreflightCalled = true;
            return Task.FromResult(PreflightDto ?? new PreflightStatusDto(attemptId, 1, "Mock Exam", "TOEIC", "TOEIC", 120, true, true, false, true, DateTimeOffset.UtcNow, []));
        }

        public SittingSyncDto? SyncDto { get; set; }
        public AttemptResumeDto? ResumeDto { get; set; }
        public SittingSyncRequestDto? LastSyncRequest { get; private set; }
        public ResumeAttemptRequest? LastResumeRequest { get; private set; }

        public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException<SittingSyncDto>(ExceptionToThrow);
            LastSyncRequest = request;
            return Task.FromResult(SyncDto ?? new SittingSyncDto(attemptId, 1800, false, null, 1, null, null, request.LastAudioOffsetMs ?? 0, request.Answers?.Count ?? 0));
        }

        public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException<AttemptResumeDto>(ExceptionToThrow);
            LastResumeRequest = request;
            return Task.FromResult(ResumeDto ?? new AttemptResumeDto(1, 1, request.AuthorizationCode, null, 1, null, null, 0, null, 1800, DateTimeOffset.UtcNow.AddMinutes(30), null, []));
        }
    }

    [Fact]
    public async Task History_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            HistoryDto = new ExamHistoryDto(
                new CertificateOptionDto("TOEIC", "TOEIC Test"),
                1, 750, 750, "B1", "Intermediate",
                [new ScoreBandItemDto("B1", "Intermediate", 550, 780, 1)],
                [new ExamHistoryItemDto(100, 1, "ETS 2024 Test 1", 1, "TOEIC", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 750, "B1", "Intermediate", [], AttemptStatusDto.Graded)])
        };

        var controller = CreateController(api);
        var result = await controller.History(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamHistoryViewModel>(viewResult.Model);
        Assert.Equal(1, model.History.TotalAttempts);
        Assert.Equal(750, model.History.BestScore);
    }

    [Fact]
    public async Task History_when_api_exception_redirects_to_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "Error", 500)
        };

        var controller = CreateController(api);
        var result = await controller.History(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task Review_returns_view_with_model()
    {
        var api = new TestApiClient
        {
            ReviewDto = new AttemptReviewDto(
                100, 1, "ETS 2024 Test 1", 1, "TOEIC", "TOEIC Test", AttemptStatusDto.Graded,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 3600, 750, "B1", "Intermediate",
                [], 2, 2, 2, 0, 0, 0, [], [])
        };

        var controller = CreateController(api);
        var result = await controller.Review(100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamReviewViewModel>(viewResult.Model);
        Assert.Equal(100, model.Review.AttemptId);
        Assert.Equal(750, model.Review.TotalScore);
    }

    [Fact]
    public async Task Review_when_not_found_returns_AttemptNotFound()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFound, "Attempt not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Review(999, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("AttemptNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Review_when_not_finished_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFinished, "Attempt not finished", 400)
        };

        var controller = CreateController(api);
        var result = await controller.Review(100, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Take), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Start_when_preflight_required_and_unverified_redirects_to_Preflight()
    {
        var api = new TestApiClient
        {
            StartResponse = new StartMockExamResponse(100, 1, AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200, false),
            PreflightDto = new PreflightStatusDto(100, 1, "Speaking Mock", "IELTS", "IELTS Academic", 120, true, true, true, false, null, ["/audio/1.mp3"])
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Preflight), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Take_when_preflight_required_and_unverified_redirects_to_Preflight()
    {
        var api = new TestApiClient
        {
            PreflightDto = new PreflightStatusDto(100, 1, "Speaking Mock", "IELTS", "IELTS Academic", 120, true, true, true, false, null, ["/audio/1.mp3"])
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Preflight), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Take_when_preflight_verified_allows_access_to_sitting_view()
    {
        var currentQ = new AttemptQuestionDto(
            1, 1, 1, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
            "Question stem", null, null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "Option A")], null, false, "multiple_choice",
            null, null, null, null, null, null, null, null, null, 0);

        var api = new TestApiClient
        {
            PreflightDto = new PreflightStatusDto(100, 1, "Listening Mock", "TOEIC", "TOEIC", 120, true, true, false, true, DateTimeOffset.UtcNow, ["/audio/1.mp3"]),
            TakeDto = new AttemptTakeDto(100, 1, "Listening Mock", "TOEIC", AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200, 1, 0, 0, currentQ, [], false)
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamTakeViewModel>(viewResult.Model);
        Assert.Equal(100, model.Take.AttemptId);
    }

    [Fact]
    public async Task Take_when_api_throws_PreflightRequired_redirects_to_Preflight()
    {
        var api = new TestApiClient
        {
            PreflightDto = new PreflightStatusDto(100, 1, "Listening Mock", "TOEIC", "TOEIC", 120, false, false, false, true, DateTimeOffset.UtcNow, []),
            ExceptionToThrow = new ApiException(ApiError.Codes.PreflightRequired, "Preflight verification required", 400)
        };

        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Preflight), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Preflight_returns_view_with_preflight_model()
    {
        var api = new TestApiClient
        {
            PreflightDto = new PreflightStatusDto(100, 1, "Speaking Mock", "IELTS", "IELTS Academic", 120, true, true, true, false, null, ["/audio/track1.mp3"])
        };

        var controller = CreateController(api);
        var result = await controller.Preflight(1, 100, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Preflight", viewResult.ViewName);
        var model = Assert.IsType<ExamPreflightViewModel>(viewResult.Model);
        Assert.Equal(100, model.Preflight.AttemptId);
        Assert.True(model.Preflight.RequiresMicrophone);
        Assert.Single(model.Preflight.AudioUrls);
    }

    [Fact]
    public async Task VerifyPreflight_calls_api_and_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            PreflightDto = new PreflightStatusDto(100, 1, "Speaking Mock", "IELTS", "IELTS Academic", 120, true, true, true, true, DateTimeOffset.UtcNow, ["/audio/track1.mp3"])
        };

        var controller = CreateController(api);
        var result = await controller.VerifyPreflight(1, 100, null, CancellationToken.None);

        Assert.True(api.VerifyPreflightCalled);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Take), redirect.ActionName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Sync_returns_json_with_remaining_seconds_and_status()
    {
        var api = new TestApiClient
        {
            SyncDto = new SittingSyncDto(100, 1500, false, 1, 1, null, null, 12000, 2)
        };

        var controller = CreateController(api);
        var request = new SittingSyncRequestDto(
            Answers: [new BatchAnswerItemDto(1, 10, false, null, null)],
            LastAudioOffsetMs: 12000);

        var result = await controller.Sync(100, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var propMap = json.Value.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(json.Value));
        Assert.Equal(100, propMap["attemptId"]);
        Assert.Equal(1500, propMap["remainingSeconds"]);
        Assert.Equal(false, propMap["isAutoSubmitted"]);
        Assert.Equal(12000, propMap["lastAudioOffsetMs"]);
        Assert.Equal(2, propMap["answersSavedCount"]);
    }

    [Fact]
    public async Task Sync_when_attempt_expired_returns_json_with_redirect()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptExpired, "Attempt expired", 400)
        };

        var controller = CreateController(api);
        var request = new SittingSyncRequestDto(Answers: []);

        var result = await controller.Sync(100, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var propMap = json.Value!.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(json.Value));
        Assert.Equal(true, propMap["isAutoSubmitted"]);
        Assert.NotNull(propMap["redirectUrl"]);
    }

    [Fact]
    public void Resume_Get_returns_view_with_model()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = controller.Resume();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResumeViewModel>(viewResult.Model);
        Assert.Null(model.AuthorizationCode);
    }

    [Fact]
    public async Task Resume_Post_when_valid_code_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            ResumeDto = new AttemptResumeDto(105, 1, "EX-ABCDEF", 1, 1, null, null, 0, null, 1800, DateTimeOffset.UtcNow.AddMinutes(30), null, [])
        };

        var controller = CreateController(api);
        var form = new ExamResumeFormModel { AuthorizationCode = "ex-abcdef" };

        var result = await controller.Resume(form, CancellationToken.None);

        Assert.Equal("EX-ABCDEF", api.LastResumeRequest?.AuthorizationCode);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Take), redirect.ActionName);
        Assert.Equal(105, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task Resume_Post_when_empty_code_returns_view_with_error()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Resume(new ExamResumeFormModel { AuthorizationCode = "" }, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResumeViewModel>(viewResult.Model);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task Resume_Post_when_code_not_found_returns_view_with_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFound, "Attempt not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Resume(new ExamResumeFormModel { AuthorizationCode = "EX-NOTFOUND" }, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResumeViewModel>(viewResult.Model);
        Assert.Contains("No active exam sitting was found", model.ErrorMessage);
    }

    [Fact]
    public async Task Take_with_split_screen_and_component_metadata_returns_model_with_sandbox_properties()
    {
        var q = new AttemptQuestionDto(
            10, 1, 200, 1, "P7", "Reading Passage 1", "reading", "Reading", ModalityDto.Reading,
            "What is the main topic of the passage?", "Full passage text here...", null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "Option A"), new AttemptOptionDto(2, "B", "Option B")],
            null, false, "multi_select", 1, null, null,
            SelectedOptionIds: [1],
            MaxSelections: 2,
            AllowNumber: true,
            AcceptedAnswersJson: "[\"answer1\"]",
            RequiredKeywords: "keyword1,keyword2",
            LayoutType: "SPLIT_HORIZONTAL",
            RequirePassage: true,
            NavigationMode: "FULL_SANDBOX",
            ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}"
        );

        var take = new AttemptTakeDto(
            100, 1, "IELTS Academic Test 1", "IELTS", AttemptStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200,
            40, 1, 0, q,
            [new NavigatorItemDto(10, 1, 1, "P7", ModalityDto.Reading, true, false, true)],
            false);

        var api = new TestApiClient { TakeDto = take };
        var controller = CreateController(api);
        var result = await controller.Take(100, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamTakeViewModel>(viewResult.Model);
        Assert.Equal(100, model.Take.AttemptId);
        Assert.Equal("SPLIT_HORIZONTAL", model.Take.CurrentQuestion.LayoutType);
        Assert.True(model.Take.CurrentQuestion.RequirePassage);
        Assert.Equal("FULL_SANDBOX", model.Take.CurrentQuestion.NavigationMode);
        Assert.Equal(2, model.Take.CurrentQuestion.MaxSelections);
        Assert.True(model.Take.CurrentQuestion.AllowNumber);
        Assert.NotNull(model.Take.CurrentQuestion.SelectedOptionIds);
        Assert.Contains(1, model.Take.CurrentQuestion.SelectedOptionIds!);
        Assert.Contains("highlighter", model.Take.CurrentQuestion.ToolsConfigJson);
    }
    [Fact]
    public async Task Take_with_system_paced_auto_returns_model_with_speaking_pacer_metadata()
    {
        var q = new AttemptQuestionDto(
            15, 1, 11, 1, "SP1", "Speaking Part 1", "speaking", "Speaking", ModalityDto.Speaking,
            "Read a text aloud.", null, null, null, null, null, null, null,
            [],
            null, false, "free_response", null, null,
            PrepSeconds: 45,
            ResponseSeconds: 45,
            NavigationMode: "SYSTEM_PACED_AUTO"
        );

        var take = new AttemptTakeDto(
            105, 1, "TOEIC Speaking Test 1", "TOEIC_SW", AttemptStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(20), 1200,
            11, 1, 0, q,
            [new NavigatorItemDto(15, 1, 1, "SP1", ModalityDto.Speaking, true, false, true)],
            false);

        var api = new TestApiClient { TakeDto = take };
        var controller = CreateController(api);
        var result = await controller.Take(105, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamTakeViewModel>(viewResult.Model);
        Assert.Equal(105, model.Take.AttemptId);
        Assert.Equal("SYSTEM_PACED_AUTO", model.Take.CurrentQuestion.NavigationMode);
        Assert.Equal(45, model.Take.CurrentQuestion.PrepSeconds);
        Assert.Equal(45, model.Take.CurrentQuestion.ResponseSeconds);
    }

    [Fact]
    public async Task UploadSpeakingAudio_with_empty_file_returns_bad_request()
    {
        var api = new TestApiClient();
        var speakingApi = new TestSpeakingClient();
        var controller = CreateController(api, speakingApi);

        var result = await controller.UploadSpeakingAudio(105, 15, null, 15000, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task UploadSpeakingAudio_with_valid_file_forwards_to_speaking_client_and_returns_ok()
    {
        var api = new TestApiClient();
        var speakingApi = new TestSpeakingClient();
        var controller = CreateController(api, speakingApi);

        var fileBytes = System.Text.Encoding.UTF8.GetBytes("fake audio bytes");
        var stream = new MemoryStream(fileBytes);
        var formFile = new FormFile(stream, 0, fileBytes.Length, "audioFile", "recording.webm")
        {
            Headers = new HeaderDictionary(),
            ContentType = "audio/webm"
        };

        var result = await controller.UploadSpeakingAudio(105, 15, formFile, 25000, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<UploadSpeakingAudioResponse>(okResult.Value);
        Assert.Equal(15, response.QuestionId);
        Assert.Equal(105, speakingApi.UploadedAttemptId);
        Assert.Equal(15, speakingApi.UploadedQuestionId);
        Assert.Equal("recording.webm", speakingApi.UploadedFileName);
        Assert.Equal(25000, speakingApi.UploadedDurationMs);
    }

    [Fact]
    public async Task GetSpeakingAudio_returns_file_stream_result()
    {
        var api = new TestApiClient();
        var speakingApi = new TestSpeakingClient();
        var controller = CreateController(api, speakingApi);

        var result = await controller.GetSpeakingAudio(105, 15, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/webm", fileResult.ContentType);
        Assert.Equal("test.webm", fileResult.FileDownloadName);
        Assert.True(fileResult.EnableRangeProcessing);
    }
}

