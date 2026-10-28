using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Tier 3: Cross-Feature Interactions Test Suite for WebMVC.
/// Validates cross-subsystem interactions: sitting lifecycle with auth claims, anti-forgery guards,
/// multi-stage sitting transitions, and diagnostic error propagation.
/// </summary>
public sealed class Tier3CrossFeatureInteractionTests
{
    // =========================================================================
    // 1. Sitting Lifecycle With Authentication & Cookie Claims
    // =========================================================================

    [Fact]
    public async Task Start_MockExam_Under_Authenticated_Learner_Redirects_To_Take_With_AttemptId()
    {
        var api = new TestCrossFeatureExamsClient
        {
            StartResponse = new StartMockExamResponse(
                AttemptId: 777,
                ExamId: 10,
                Status: AttemptStatusDto.InProgress,
                StartedAt: DateTimeOffset.UtcNow,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(2),
                RemainingSeconds: 7200,
                IsAutoSubmitted: false)
        };

        var controller = CreateController(api, userId: 15, email: "learner.sat@test.local");
        var result = await controller.Start(10, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Take), redirect.ActionName);
        Assert.Equal(777, redirect.RouteValues?["attemptId"]);
    }

    [Fact]
    public async Task FixedAuthentication_Provides_Deterministic_Ticket_And_Claims()
    {
        var identity = new ClaimsIdentity(AuthClaims.For(88, "fixed@demo.local", Roles.Learner, "Fixed Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "header.payload.sig" });

        var authService = new FixedAuthentication(principal, properties);
        var httpContext = new DefaultHttpContext();

        var result = await authService.AuthenticateAsync(httpContext, "Cookies");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Ticket);
        Assert.Equal("88", result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value);
        Assert.Equal("fixed@demo.local", result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimEmail)?.Value);
        Assert.Equal(Roles.Learner, result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimRole)?.Value);
    }

    // =========================================================================
    // 2. Anti-Forgery & Mutation Protection Guards
    // =========================================================================

    [Theory]
    [InlineData(typeof(ExamsController), "Start")]
    [InlineData(typeof(ExamsController), "Submit")]
    [InlineData(typeof(ExamsController), "Resume")]
    public void Mutation_Endpoints_Declare_HttpPost_Attribute(Type controllerType, string actionName)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var httpPostMethod = System.Array.Find(methods, m => m.Name == actionName && m.GetCustomAttribute<HttpPostAttribute>() != null);
        Assert.NotNull(httpPostMethod);
    }

    // =========================================================================
    // 3. Multi-Stage Sitting State Transitions & ViewModel Transformation
    // =========================================================================

    [Fact]
    public async Task Take_With_MultiUnit_Exam_Provides_Encapsulated_ViewModel_With_ActiveUnit()
    {
        var question = new AttemptQuestionDto(
            1, 1, 100, 1, "P1", "Photos", "listening", "Listening", ModalityDto.Listening,
            "Look at the photo", null, null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "Text A"), new AttemptOptionDto(2, "B", "Text B")],
            SelectedOptionId: 1,
            IsFlagged: false);

        var attemptTake = new AttemptTakeDto(
            AttemptId: 801,
            ExamId: 20,
            ExamName: "Adaptive Stage Exam",
            CertCode: "TOEIC",
            Status: AttemptStatusDto.InProgress,
            StartedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(2),
            RemainingSeconds: 2700,
            TotalQuestions: 40,
            AnsweredCount: 1,
            FlaggedCount: 0,
            CurrentQuestion: question,
            Navigator:
            [
                new NavigatorItemDto(1, 1, 1, "P1", ModalityDto.Listening, true, false, true, false)
            ],
            IsAutoSubmitted: false,
            Questions: [question],
            Phases: [],
            ActiveSkillId: 1,
            ActiveUnitNumber: 2,
            ActiveGroupId: 5,
            PhaseDeadlineAt: DateTimeOffset.UtcNow.AddMinutes(45));

        var api = new TestCrossFeatureExamsClient { TakeDto = attemptTake };
        var controller = CreateController(api);

        var result = await controller.Take(801, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamTakeViewModel>(viewResult.Model);
        Assert.Equal(801, model.Take.AttemptId);
        Assert.Equal(2, model.Take.ActiveUnitNumber);
        Assert.Equal(2700, model.Take.RemainingSeconds);
        Assert.NotNull(model.Take.Questions);
        Assert.Single(model.Take.Questions);
        Assert.Equal(1, model.Take.Questions[0].SelectedOptionId);
    }

    [Fact]
    public async Task Finished_Attempt_In_Take_Redirects_To_Result_View()
    {
        var dummyQuestion = new AttemptQuestionDto(
            1, 1, 100, 1, "P1", "Photos", "listening", "Listening", ModalityDto.Listening,
            "Look at the photo", null, null, null, null, null, null, null,
            [], null, false);

        var attemptTake = new AttemptTakeDto(
            AttemptId: 900,
            ExamId: 25,
            ExamName: "Finished Exam",
            CertCode: "TOEIC",
            Status: AttemptStatusDto.Submitted,
            StartedAt: DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt: DateTimeOffset.UtcNow,
            RemainingSeconds: 0,
            TotalQuestions: 10,
            AnsweredCount: 10,
            FlaggedCount: 0,
            CurrentQuestion: dummyQuestion,
            Navigator: [],
            IsAutoSubmitted: false,
            Questions: [],
            Phases: [],
            ActiveSkillId: null,
            ActiveUnitNumber: 1,
            ActiveGroupId: null,
            PhaseDeadlineAt: null);

        var api = new TestCrossFeatureExamsClient { TakeDto = attemptTake };
        var controller = CreateController(api);

        var result = await controller.Take(900, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Result), redirect.ActionName);
        Assert.Equal(900, redirect.RouteValues?["attemptId"]);
    }

    // =========================================================================
    // 4. Client Pipeline & Error Cascading
    // =========================================================================

    [Fact]
    public async Task InsufficientCredits_ApiException_In_Start_Redirects_To_Instructions_With_ShellError()
    {
        var api = new TestCrossFeatureExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "You do not have enough credits", 400)
        };
        var controller = CreateController(api);

        var result = await controller.Start(50, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Instructions), redirect.ActionName);
        Assert.Equal(50, redirect.RouteValues?["id"]);
        Assert.Equal("You do not have enough credits to start this mock exam.", controller.TempData["ErrorMessage"]);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ExamsController CreateController(TestCrossFeatureExamsClient api, int userId = 1, string email = "test@learner.local")
    {
        var identity = new ClaimsIdentity(AuthClaims.For(userId, email, Roles.Learner, "Learner User"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var controller = new ExamsController(api, currentUser, NullLogger<ExamsController>.Instance, new WebMVC.Formatters.DateTimeFormatter(new HttpContextAccessor { HttpContext = httpContext }), null)
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

    private sealed class TestCrossFeatureExamsClient : IMockExamsClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public StartMockExamResponse? StartResponse { get; set; }
        public AttemptTakeDto? TakeDto { get; set; }

        public Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartMockExamResponse>(ExceptionToThrow) :
            Task.FromResult(StartResponse ?? new StartMockExamResponse(100, examId, AttemptStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200, false));

        public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTakeDto>(ExceptionToThrow) :
            Task.FromResult(TakeDto!);

        public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PreflightStatusDto>(ExceptionToThrow) :
            Task.FromResult(new PreflightStatusDto(attemptId, 1, "Test Exam", "TOEIC", "TOEIC", 120, false, false, false, true, DateTimeOffset.UtcNow, []));

        public Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
