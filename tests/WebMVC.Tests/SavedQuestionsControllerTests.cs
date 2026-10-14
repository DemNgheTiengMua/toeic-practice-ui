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

public sealed class SavedQuestionsControllerTests
{
    private static SavedQuestionsViewDto SampleView() => new(
        new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
        TotalSavedCount: 2,
        ManualSavedCount: 1,
        IncorrectSavedCount: 1,
        Questions:
        [
            new SavedQuestionItemDto(
                QuestionId: 101,
                Source: "manual",
                SavedAt: DateTimeOffset.UtcNow,
                SectionId: 5,
                SectionCode: "P5",
                SectionName: "Incomplete Sentences",
                CategoryId: 1,
                CategoryName: "Verb tense",
                Stem: "The manager asked that the report ______ by Friday.",
                Passage: null,
                ImagePath: null,
                AudioPath: null,
                Hint: "Look at the main verb.",
                Explanation: "Use future tense will review.",
                Transcript: null,
                Options:
                [
                    new SavedQuestionOptionDto(1, "A", "review", false),
                    new SavedQuestionOptionDto(2, "B", "will review", true)
                ]),
            new SavedQuestionItemDto(
                QuestionId: 102,
                Source: "incorrect",
                SavedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                SectionId: 5,
                SectionCode: "P5",
                SectionName: "Incomplete Sentences",
                CategoryId: 2,
                CategoryName: "Prepositions",
                Stem: "Inquiries must be resolved ______ forty-eight hours.",
                Passage: null,
                ImagePath: null,
                AudioPath: null,
                Hint: "Preposition of time.",
                Explanation: "Within indicates duration.",
                Transcript: null,
                Options:
                [
                    new SavedQuestionOptionDto(3, "A", "within", true),
                    new SavedQuestionOptionDto(4, "B", "between", false)
                ])
        ],
        Sections: [new SavedQuestionSectionFilterDto(5, "P5", "Incomplete Sentences", 2)],
        Categories: [new SavedQuestionCategoryFilterDto(1, "Verb tense", 1), new SavedQuestionCategoryFilterDto(2, "Prepositions", 1)]);

    [Fact]
    public async Task Index_returns_view_with_saved_questions_model()
    {
        var api = new TestApiClient
        {
            SavedQuestions = SampleView(),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, null, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionsIndexViewModel>(viewResult.Model);
        Assert.Equal(2, model.Data.TotalSavedCount);
        Assert.Equal("TOEIC", model.Data.Certificate.Code);
        Assert.Equal(2, model.Data.Questions.Count);
    }

    [Fact]
    public async Task Index_forwards_filter_query_params()
    {
        var api = new TestApiClient
        {
            SavedQuestions = SampleView(),
        };

        var controller = CreateController(api);
        var result = await controller.Index("manual", 5, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionsIndexViewModel>(viewResult.Model);
        Assert.Equal("manual", model.CurrentSource);
        Assert.Equal(5, model.CurrentSectionId);
        Assert.Equal(1, model.CurrentCategoryId);
    }

    [Fact]
    public async Task Index_redirects_to_dashboard_on_api_failure()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Unexpected, "Error", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, null, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Review_returns_view_with_question_details()
    {
        var sampleQuestion = SampleView().Questions[0];
        var api = new TestApiClient
        {
            SavedItem = sampleQuestion,
        };

        var controller = CreateController(api);
        var result = await controller.Review(101, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionReviewViewModel>(viewResult.Model);
        Assert.Equal(101, model.Question.QuestionId);
        Assert.Equal("Use future tense will review.", model.Question.Explanation);
    }

    [Fact]
    public async Task Review_returns_json_when_ajax_requested()
    {
        var sampleQuestion = SampleView().Questions[0];
        var api = new TestApiClient
        {
            SavedItem = sampleQuestion,
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Review(101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<SavedQuestionItemDto>(jsonResult.Value);
        Assert.Equal(101, dto.QuestionId);
    }

    [Fact]
    public async Task Review_redirects_to_index_when_not_found()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.SavedQuestionNotFound, "Not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Review(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SavedQuestionsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Save_returns_json_when_ajax_requested()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, "manual", DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Save(101, "manual", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("isSaved").GetBoolean());
        Assert.Equal("manual", val.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Remove_returns_json_when_ajax_requested()
    {
        var api = new TestApiClient
        {
            UnsaveResult = new SaveQuestionResultDto(false, null, null),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Remove(101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.False(val.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task Toggle_unsaves_when_currently_saved()
    {
        var api = new TestApiClient
        {
            UnsaveResult = new SaveQuestionResultDto(false, null, null),
        };

        var controller = CreateController(api);
        var result = await controller.Toggle(101, currentlySaved: true, source: null, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.False(val.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task Toggle_saves_when_not_currently_saved()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, "manual", DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        var result = await controller.Toggle(101, currentlySaved: false, source: "manual", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("isSaved").GetBoolean());
        Assert.Equal("manual", val.GetProperty("source").GetString());
    }

    private static SavedQuestionsController CreateController(TestApiClient api)
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

        var controller = new SavedQuestionsController(api, currentUser, NullLogger<SavedQuestionsController>.Instance)
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

        public SavedQuestionsViewDto? SavedQuestions { get; set; }

        public SavedQuestionItemDto? SavedItem { get; set; }

        public SaveQuestionResultDto? SaveResult { get; set; }

        public SaveQuestionResultDto? UnsaveResult { get; set; }

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

        public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source, int? sectionId, int? categoryId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SavedQuestionsViewDto>(ExceptionToThrow) : Task.FromResult(SavedQuestions!);

        public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SavedQuestionItemDto>(ExceptionToThrow) : Task.FromResult(SavedItem!);

        public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveQuestionResultDto>(ExceptionToThrow) : Task.FromResult(SaveResult!);

        public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int questionId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveQuestionResultDto>(ExceptionToThrow) : Task.FromResult(UnsaveResult!);
    }
}
