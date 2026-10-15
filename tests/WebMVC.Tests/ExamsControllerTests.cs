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
                    new MockExamSectionInfoDto(1, "listening", "Listening", ModalityDto.Listening, "P1", "Part 1", 6, 45, 1)
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
            "Look at the picture.", null, null, null, null, null,
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
        var q = new AttemptQuestionDto(10, 1, 200, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, [], null, false);
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

    private static ExamsController CreateController(TestApiClient api)
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

        var controller = new ExamsController(api, currentUser, NullLogger<ExamsController>.Instance)
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
        public MockExamListViewDto? MockExamsCatalog { get; set; }
        public MockExamInstructionsDto? Instructions { get; set; }
        public StartMockExamResponse? StartResponse { get; set; }
        public AttemptTakeDto? TakeDto { get; set; }
        public SaveAnswerResultDto? SaveResult { get; set; }
        public SubmitAttemptResponse? SubmitResponse { get; set; }
        public AttemptResultDto? ResultDto { get; set; }

        public Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthUserDto> GetMeAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProfileDto> GetProfileAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ChangePasswordAsync(ChangePasswordRequest request, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResendActivationAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LearnerContextDto> GetLearnerContextAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SwitchCertificateAsync(string bearerToken, string code, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LearnerDashboardDto> GetDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<MockExamListViewDto> GetMockExamsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamListViewDto>(ExceptionToThrow) : Task.FromResult(MockExamsCatalog!);

        public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamInstructionsDto>(ExceptionToThrow) : Task.FromResult(Instructions!);

        public Task<StartMockExamResponse> StartMockExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartMockExamResponse>(ExceptionToThrow) : Task.FromResult(StartResponse!);

        public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTakeDto>(ExceptionToThrow) : Task.FromResult(TakeDto!);

        public Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveAnswerResultDto>(ExceptionToThrow) : Task.FromResult(SaveResult!);

        public Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SubmitAttemptResponse>(ExceptionToThrow) : Task.FromResult(SubmitResponse!);

        public Task CancelAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;

        public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptResultDto>(ExceptionToThrow) : Task.FromResult(ResultDto!);

        public ExamHistoryDto? HistoryDto { get; set; }
        public AttemptReviewDto? ReviewDto { get; set; }

        public Task<ExamHistoryDto> GetExamHistoryAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<ExamHistoryDto>(ExceptionToThrow) : Task.FromResult(HistoryDto!);

        public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptReviewDto>(ExceptionToThrow) : Task.FromResult(ReviewDto!);

        public AiInsightDto? InsightDto { get; set; }
        public bool RetryInsightCalled { get; set; }

        public Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AiInsightDto>(ExceptionToThrow) : Task.FromResult(InsightDto!);

        public Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException(ExceptionToThrow);
            RetryInsightCalled = true;
            return Task.CompletedTask;
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
}

