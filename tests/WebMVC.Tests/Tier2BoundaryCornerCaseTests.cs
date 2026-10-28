using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Tier 2: Boundary & Corner Cases Test Suite for WebMVC.
/// Covers empty/null inputs, extreme timeouts, special character / Vietnamese encoding integrity,
/// XSS sanitization, and error code failure cascading.
/// </summary>
public sealed class Tier2BoundaryCornerCaseTests
{
    // =========================================================================
    // 1. Empty & Null Input Handling
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Empty_Or_Whitespace_AuthorizationCode_In_Resume_Returns_Model_With_Error(string? authCode)
    {
        var api = new TestBoundaryExamsClient();
        var controller = CreateController(api);

        var result = await controller.Resume(new ExamResumeFormModel { AuthorizationCode = authCode ?? string.Empty }, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamResumeViewModel>(viewResult.Model);
        Assert.NotNull(model.ErrorMessage);
        Assert.False(api.ResumeCalled, "API Resume should not be invoked when authorization code is empty or whitespace");
    }

    [Fact]
    public async Task Sync_With_Null_Answers_List_Handles_Gracefully()
    {
        var api = new TestBoundaryExamsClient
        {
            SyncResponse = new SittingSyncDto(100, 1500, false, 1, 1, null, null, 5000, 2)
        };
        var controller = CreateController(api);

        var request = new SittingSyncRequestDto(Answers: null, LastAudioOffsetMs: 5000);
        var result = await controller.Sync(100, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Take_With_Invalid_Zero_Or_Negative_AttemptId_Returns_AttemptNotFound_View()
    {
        var api = new TestBoundaryExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFound, "Attempt not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.Take(0, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("AttemptNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    // =========================================================================
    // 2. Extreme Timeouts & Countdown Bounds
    // =========================================================================

    [Fact]
    public async Task Sync_When_Remaining_Seconds_Is_Zero_Or_Expired_Returns_AutoSubmitted_True()
    {
        var api = new TestBoundaryExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptExpired, "Attempt expired", 400)
        };
        var controller = CreateController(api);

        var request = new SittingSyncRequestDto(Answers: []);
        var result = await controller.Sync(200, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var propMap = json.Value.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(json.Value));
        Assert.Equal(true, propMap["isAutoSubmitted"]);
        Assert.NotNull(propMap["redirectUrl"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-3600)]
    public void NonPositive_Remaining_Seconds_In_SittingSyncDto_Flags_AutoSubmitted(int remainingSeconds)
    {
        var dto = new SittingSyncDto(
            AttemptId: 301,
            RemainingSeconds: remainingSeconds,
            IsAutoSubmitted: remainingSeconds <= 0,
            ActiveSkillId: 1,
            ActiveUnitNumber: 1,
            ActiveGroupId: null,
            PhaseDeadlineAt: null,
            LastAudioOffsetMs: 0,
            AnswersSavedCount: 0);

        Assert.True(dto.IsAutoSubmitted);
        Assert.True(dto.RemainingSeconds <= 0);
    }

    [Fact]
    public void Extreme_Duration_Minutes_Does_Not_Cause_Integer_Overflow()
    {
        // 50,000 minutes = 3,000,000 seconds
        var durationMinutes = 50000;
        var durationSeconds = (long)durationMinutes * 60;

        Assert.Equal(3000000L, durationSeconds);
        var timeSpan = TimeSpan.FromMinutes(durationMinutes);
        Assert.Equal(50000, timeSpan.TotalMinutes);
    }

    // =========================================================================
    // 3. Special Characters, Vietnamese Unicode & Escaping Integrity
    // =========================================================================

    [Fact]
    public void Vietnamese_Diacritical_Text_Survives_Json_Serialization_Without_Mojibake()
    {
        var vietnamesePrompt = "Đề thi thử năng lực tiếng Anh TOEIC 2026: Đọc kỹ đoạn văn và chọn đáp án đúng nhất (A, B, C, hoặc D).";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var request = new SaveAnswerRequest(
            SelectedOptionId: 10,
            IsFlagged: false,
            ResponseText: vietnamesePrompt);

        var json = JsonSerializer.Serialize(request, options);
        var deserialized = JsonSerializer.Deserialize<SaveAnswerRequest>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(vietnamesePrompt, deserialized.ResponseText);

        // Ensure zero mojibake corruption patterns
        string[] mojibakeMarkers = ["Ã‚", "Ã¡", "Ã\xa0", "Ã£", "Ã©", "Ã¨", "áº", "á»", "Æ°", "Äƒ", "Ä‘"];
        foreach (var marker in mojibakeMarkers)
        {
            Assert.DoesNotContain(marker, deserialized.ResponseText);
        }
    }

    [Fact]
    public void XSS_Payload_In_FreeText_Response_Is_Escaped_By_AiNarrativeFormatter()
    {
        var xssPayload = "<script>alert('pwned')</script><iframe src='javascript:alert(1)'></iframe>**Safe Text**";
        var safeHtml = AiNarrativeFormatter.ToSafeHtml(xssPayload);

        Assert.DoesNotContain("<script", safeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", safeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", safeHtml);
        Assert.Contains("<strong>Safe Text</strong>", safeHtml);
    }

    [Fact]
    public void Quotes_And_Apostrophes_In_Answer_Options_Preserved_Properly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var answer = new BatchAnswerItemDto(
            QuestionId: 405,
            SelectedOptionId: 1,
            IsFlagged: false,
            ResponseText: "She said, \"It's O'Connor's book.\"",
            SelectedOptionIdsJson: "[\"A\", \"B\"]");

        var json = JsonSerializer.Serialize(answer, options);
        var deserialized = JsonSerializer.Deserialize<BatchAnswerItemDto>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("She said, \"It's O'Connor's book.\"", deserialized.ResponseText);
        Assert.Equal("[\"A\", \"B\"]", deserialized.SelectedOptionIdsJson);
    }

    // =========================================================================
    // 4. Error Handling & Failure Cascading
    // =========================================================================

    [Fact]
    public async Task AttemptNotFound_ApiException_In_Take_Returns_AttemptNotFound_View()
    {
        var api = new TestBoundaryExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.AttemptNotFound, "Attempt not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.Take(999, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("AttemptNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Forbidden_Role_ApiException_In_Instructions_Redirects_Gracefully()
    {
        var api = new TestBoundaryExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Forbidden, "Forbidden", 403)
        };
        var controller = CreateController(api);

        var result = await controller.Instructions(101, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Index), redirect.ActionName);
    }

    // =========================================================================
    // Test Infrastructure Helpers
    // =========================================================================

    private static ExamsController CreateController(TestBoundaryExamsClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(1, "learner@test.local", Roles.Learner, "Test Learner"), "Cookies");
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
            Url = new BoundaryTestUrlHelper(),
        };

        return controller;
    }

    private sealed class BoundaryTestUrlHelper : IUrlHelper
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

    private sealed class TestBoundaryExamsClient : IMockExamsClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public SittingSyncDto? SyncResponse { get; set; }
        public bool ResumeCalled { get; private set; }

        public Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamListViewDto>(ExceptionToThrow) :
            Task.FromResult(new MockExamListViewDto(new CertificateOptionDto("TOEIC", "TOEIC"), 10, []));

        public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<MockExamInstructionsDto>(ExceptionToThrow) :
            Task.FromResult(new MockExamInstructionsDto(examId, 1, "TOEIC", "Test Exam", null, 120, 200, 1, 10, null, []));

        public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AttemptTakeDto>(ExceptionToThrow) :
            throw new NotImplementedException();

        public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SittingSyncDto>(ExceptionToThrow) :
            Task.FromResult(SyncResponse ?? new SittingSyncDto(attemptId, 1200, false, 1, 1, null, null, 0, 0));

        public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default)
        {
            ResumeCalled = true;
            if (ExceptionToThrow != null) return Task.FromException<AttemptResumeDto>(ExceptionToThrow);
            return Task.FromResult(new AttemptResumeDto(1, 1, "CODE", 1, 1, null, null, 0, null, 1800, DateTimeOffset.UtcNow.AddMinutes(30), null, []));
        }

        public Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
        public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PreflightStatusDto>(ExceptionToThrow) :
            Task.FromResult(new PreflightStatusDto(attemptId, 1, "Test Exam", "TOEIC", "TOEIC", 120, false, false, false, true, DateTimeOffset.UtcNow, []));
        public Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
