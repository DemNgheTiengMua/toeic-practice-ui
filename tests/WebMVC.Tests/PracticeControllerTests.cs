using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;

namespace WebMVC.Tests;

public sealed class PracticeControllerTests
{
    [Fact]
    public async Task Index_returns_view_with_catalog()
    {
        var api = new TestApiClient
        {
            Catalog = new PracticeCatalogDto(
                new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
                [new PracticeSectionDto(1, "listening", "Listening", ModalityDto.Listening, "P1", "Part 1", 4, 6, 6, 80, 5, 1)],
                [new PracticeSectionDto(5, "reading", "Reading", ModalityDto.Reading, "P5", "Part 5", 4, 30, 20, 60, 10, 5)],
                [],
                null),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PracticeCatalogViewModel>(viewResult.Model);
        Assert.Single(model.Catalog.ListeningSections);
        Assert.Single(model.Catalog.ReadingSections);
    }

    [Fact]
    public async Task Start_creates_session_and_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            Session = new PracticeSessionDto(
                42, 5, "P5", "Part 5", ModalityDto.Reading, 20, PracticeStatusDto.InProgress, 0, QuestionSourceDto.All, FeedbackModeDto.Immediate, DateTimeOffset.UtcNow, null),
        };

        var controller = CreateController(api);
        var result = await controller.Start(new StartPracticeFormModel { SectionId = 5, QuestionCount = 20 }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PracticeController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["sessionId"]);
    }

    [Fact]
    public async Task Take_with_finished_session_redirects_to_Summary()
    {
        var api = new TestApiClient
        {
            Session = new PracticeSessionDto(
                42, 5, "P5", "Part 5", ModalityDto.Reading, 20, PracticeStatusDto.Finished, 15, QuestionSourceDto.All, FeedbackModeDto.Immediate, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PracticeController.Summary), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["sessionId"]);
    }

    [Fact]
    public async Task Take_with_active_session_returns_question_view()
    {
        var api = new TestApiClient
        {
            Session = new PracticeSessionDto(
                42, 5, "P5", "Part 5", ModalityDto.Reading, 20, PracticeStatusDto.InProgress, 0, QuestionSourceDto.All, FeedbackModeDto.Immediate, DateTimeOffset.UtcNow, null),
            Question = new PracticeQuestionDto(
                42, "Part 5", 101, 1, 20, "Choose the correct word.", "Grammar", "Think about tense.", false,
                [new PracticeOptionDto(1, "A", "run"), new PracticeOptionDto(2, "B", "ran")],
                null, null, null, null, null, null, null, null, null),
        };

        var controller = CreateController(api);
        var result = await controller.Take(42, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PracticeTakeViewModel>(viewResult.Model);
        Assert.Equal(101, model.Question.QuestionId);
        Assert.Equal(1, model.Question.DisplayOrder);
    }

    [Fact]
    public async Task Answer_form_post_shows_explanation_for_current_question()
    {
        var api = new TestApiClient
        {
            GradedResult = new GradedAnswerDto(true, 1, 1, "Explanation", null, false, 2),
        };

        var controller = CreateController(api);
        var result = await controller.Answer(
            42, 101, new SubmitAnswerFormModel { SelectedOptionId = 1 }, 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PracticeController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["sessionId"]);
        Assert.Equal(1, redirect.RouteValues?["q"]);
    }

    [Fact]
    public async Task Skip_form_post_redirects_to_next_question()
    {
        var api = new TestApiClient
        {
            GradedResult = new GradedAnswerDto(null, null, 1, null, null, false, 2),
        };

        var controller = CreateController(api);
        var result = await controller.Answer(
            42, 101, new SubmitAnswerFormModel { Action = "skip" }, 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PracticeController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["sessionId"]);
        Assert.Equal(2, redirect.RouteValues?["q"]);
    }

    [Fact]
    public async Task Skip_form_post_on_last_question_redirects_to_Summary()
    {
        var api = new TestApiClient
        {
            GradedResult = new GradedAnswerDto(null, null, 1, null, null, true, null),
        };

        var controller = CreateController(api);
        var result = await controller.Answer(
            42, 120, new SubmitAnswerFormModel { Action = "skip" }, 20, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PracticeController.Summary), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["sessionId"]);
    }

    [Fact]
    public async Task Hint_ajax_post_returns_json_with_hint()
    {
        var api = new TestApiClient
        {
            HintResponse = new RevealHintResponse("Look at the time marker.", true),
        };

        var controller = CreateController(api);
        var result = await controller.Hint(42, 101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);
    }

    [Fact]
    public async Task Summary_returns_view_with_summary()
    {
        var api = new TestApiClient
        {
            Summary = new PracticeSummaryDto(
                42, "P5", "Part 5", ModalityDto.Reading, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 20, 19, 15, 4, 1, 79, 3, 2, 1, []),
        };

        var controller = CreateController(api);
        var result = await controller.Summary(42, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PracticeSummaryDto>(viewResult.Model);
        Assert.Equal(79, model.AccuracyPercentage);
        Assert.Equal(1, model.SkippedCount);
    }

    [Fact]
    public async Task Take_when_session_not_found_returns_404_view()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PracticeSessionNotFound, "Not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Take(999, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("SessionNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Summary_when_session_not_found_returns_404_view()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PracticeSessionNotFound, "Not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Summary(999, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("SessionNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task GetInsight_returns_ok_with_insight_dto()
    {
        var sampleInsight = new AiInsightDto(
            1, 7, 1, AiInsightScopeDto.PracticeSession, 42, null,
            "Great practice", AiInsightTrendDto.Improving, AiInsightStatusDto.Done,
            null, "fake-ai-insight-v1", 140, 110, "fingerprint",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        var api = new TestApiClient { Insight = sampleInsight };
        var controller = CreateController(api);

        var result = await controller.GetInsight(42, CancellationToken.None);
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

        var result = await controller.RetryInsight(42, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.True(api.RetryInsightCalled);
    }

    [Fact]
    public async Task Summary_prefetches_insight_into_viewbag()
    {
        var sampleInsight = new AiInsightDto(
            1, 7, 1, AiInsightScopeDto.PracticeSession, 42, null,
            "Pre-fetched insight", AiInsightTrendDto.Improving, AiInsightStatusDto.Done,
            null, "fake-ai-insight-v1", 140, 110, "fingerprint",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);

        var api = new TestApiClient
        {
            Summary = new PracticeSummaryDto(
                42, "P5", "Part 5", ModalityDto.Reading, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 20, 19, 15, 4, 1, 79, 3, 2, 1, []),
            Insight = sampleInsight
        };

        var controller = CreateController(api);
        var result = await controller.Summary(42, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var insightInViewBag = Assert.IsType<AiInsightDto>(viewResult.ViewData["Insight"]);
        Assert.Equal("Pre-fetched insight", insightInViewBag.Narrative);
    }

    private static PracticeController CreateController(TestApiClient api)
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

        var controller = new PracticeController(api, currentUser, NullLogger<PracticeController>.Instance)
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

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
    }

    private sealed class TestApiClient : ICompanionApiClient
    {
        public Exception? ExceptionToThrow { get; set; }

        public PracticeCatalogDto? Catalog { get; set; }

        public PracticeSessionDto? Session { get; set; }

        public PracticeQuestionDto? Question { get; set; }

        public RevealHintResponse? HintResponse { get; set; }

        public GradedAnswerDto? GradedResult { get; set; }

        public PracticeSummaryDto? Summary { get; set; }

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

        public Task<PracticeCatalogDto> GetPracticeCatalogAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PracticeCatalogDto>(ExceptionToThrow) : Task.FromResult(Catalog!);

        public Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PracticeSessionDto>(ExceptionToThrow) : Task.FromResult(Session!);

        public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PracticeSessionDto>(ExceptionToThrow) : Task.FromResult(Session!);

        public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PracticeQuestionDto>(ExceptionToThrow) : Task.FromResult(Question!);

        public Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<RevealHintResponse>(ExceptionToThrow) : Task.FromResult(HintResponse!);

        public Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<GradedAnswerDto>(ExceptionToThrow) : Task.FromResult(GradedResult!);

        public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PracticeSummaryDto>(ExceptionToThrow) : Task.FromResult(Summary!);

        public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public AiInsightDto? Insight { get; set; }
        public bool RetryInsightCalled { get; set; }

        public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AiInsightDto>(ExceptionToThrow) : Task.FromResult(Insight!);

        public Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) return Task.FromException(ExceptionToThrow);
            RetryInsightCalled = true;
            return Task.CompletedTask;
        }
    }
}
