using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Tier 1: Feature Coverage Test Suite for WebMVC.
/// Validates primary behavior, layout sections, pure presentation formatting, DTO contracts, and client interfaces.
/// Enforces >= 5 test cases per feature area.
/// </summary>
public sealed class Tier1FeatureCoverageTests
{
    // =========================================================================
    // Area 1: Layout Sections & Shared Layout Architecture (>= 5 cases)
    // =========================================================================

    [Fact]
    public void Area1_Layout_RootLayout_Contains_Html5DocType_And_StandardTokenCss()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var layoutPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_Layout.cshtml");
        Assert.True(File.Exists(layoutPath), "_Layout.cshtml must exist");

        var html = File.ReadAllText(layoutPath);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<meta name=\"viewport\"", html);
        Assert.Contains("tokens.css", html);
        Assert.Contains("base.css", html);
        Assert.Contains("components.css", html);
        Assert.Contains("@RenderBody()", html);
    }

    [Fact]
    public void Area1_Layout_LearnerLayout_Contains_Sidebar_And_DesignSystemCss()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var learnerLayoutPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_LearnerLayout.cshtml");
        Assert.True(File.Exists(learnerLayoutPath), "_LearnerLayout.cshtml must exist");

        var html = File.ReadAllText(learnerLayoutPath);
        Assert.Contains("tokens.css", html);
        Assert.Contains("base.css", html);
        Assert.Contains("components.css", html);
        Assert.Contains("@RenderBody()", html);
        Assert.Contains("Scripts", html);
    }

    [Fact]
    public void Area1_Layout_AdminLayout_Contains_AdminHeader_And_ScriptSection()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminLayoutPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_AdminLayout.cshtml");
        Assert.True(File.Exists(adminLayoutPath), "_AdminLayout.cshtml must exist");

        var html = File.ReadAllText(adminLayoutPath);
        Assert.Contains("tokens.css", html);
        Assert.Contains("base.css", html);
        Assert.Contains("components.css", html);
        Assert.Contains("@RenderBody()", html);
        Assert.Contains("@await RenderSectionAsync(\"Scripts\"", html);
    }

    [Fact]
    public void Area1_Layout_RootViewStart_Specifies_StandardLayout()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewStartPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "_ViewStart.cshtml");
        Assert.True(File.Exists(viewStartPath), "_ViewStart.cshtml must exist in Views root");

        var content = File.ReadAllText(viewStartPath);
        Assert.Matches(@"Layout\s*=\s*""_Layout"";?", content);
    }

    [Fact]
    public void Area1_Layout_AdminViewStart_Specifies_AdminLayout()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminViewStart = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "_ViewStart.cshtml");
        Assert.True(File.Exists(adminViewStart), "_ViewStart.cshtml must exist in Views/Admin");

        var content = File.ReadAllText(adminViewStart);
        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", content);
    }

    [Fact]
    public void Area1_Layout_AllSharedLayouts_Declare_Styles_Section()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var layouts = new[]
        {
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_Layout.cshtml"),
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_LearnerLayout.cshtml"),
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_AdminLayout.cshtml"),
        };

        foreach (var layoutPath in layouts)
        {
            Assert.True(File.Exists(layoutPath), $"{Path.GetFileName(layoutPath)} must exist");
            var html = File.ReadAllText(layoutPath);
            Assert.Matches(@"@await\s+RenderSectionAsync\(\s*""Styles""\s*,\s*required:\s*false\s*\)", html);
        }
    }

    [Fact]
    public void Area1_Layout_AllSharedLayouts_Eliminate_CurrentUser_Injection()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var layouts = new[]
        {
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_Layout.cshtml"),
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_LearnerLayout.cshtml"),
            Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_AdminLayout.cshtml"),
        };

        foreach (var layoutPath in layouts)
        {
            var html = File.ReadAllText(layoutPath);
            Assert.DoesNotMatch(@"@inject\s+CurrentUser", html);
        }
    }

    [Fact]
    public void Area1_Layout_LearnerLayout_Eliminates_Static_LearnerShell_Get()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var layoutPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_LearnerLayout.cshtml");
        var html = File.ReadAllText(layoutPath);
        Assert.DoesNotMatch(@"LearnerShell\.Get\(", html);
    }

    [Fact]
    public void Area1_Layout_AiInsightPanel_AssetSeparation_And_LineCount_Bound()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_AiInsightPanel.cshtml");
        var cssPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components", "ai-insight-panel.css");
        var jsPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "js", "components", "ai-insight-panel.js");

        Assert.True(File.Exists(viewPath), "_AiInsightPanel.cshtml must exist");
        Assert.True(File.Exists(cssPath), "ai-insight-panel.css must exist");
        Assert.True(File.Exists(jsPath), "ai-insight-panel.js must exist");

        var viewContent = File.ReadAllText(viewPath);
        Assert.DoesNotContain("<style>", viewContent);
        Assert.DoesNotContain("<script>", viewContent);
        Assert.Contains("ai-insight-panel.css", viewContent);
        Assert.Contains("ai-insight-panel.js", viewContent);

        var lineCount = File.ReadAllLines(viewPath).Length;
        Assert.True(lineCount < 250, $"_AiInsightPanel.cshtml line count ({lineCount}) must be under 250 lines");
    }

    // =========================================================================
    // Area 2: Pure Presentation, ViewModels & Formatters (>= 5 cases)
    // =========================================================================

    [Fact]
    public void Area2_Presentation_DateTimeFormatter_Formats_Instant_Across_TimeZones()
    {
        var instant = new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);

        var utc = DateTimeFormatter.FormatInZone(instant, "UTC", "yyyy-MM-dd HH:mm");
        var hcmc = DateTimeFormatter.FormatInZone(instant, "Asia/Ho_Chi_Minh", "yyyy-MM-dd HH:mm");
        var tokyo = DateTimeFormatter.FormatInZone(instant, "Asia/Tokyo", "yyyy-MM-dd HH:mm");

        Assert.Equal("2026-10-26 14:00", utc);
        Assert.Equal("2026-10-26 21:00", hcmc);
        Assert.Equal("2026-10-26 23:00", tokyo);
    }

    [Fact]
    public void Area2_Presentation_CurrencyFormatter_Formats_Vnd_Currency()
    {
        Assert.Equal("0 ₫", CurrencyFormatter.FormatVnd(0));
        Assert.Equal("49,000 ₫", CurrencyFormatter.FormatVnd(49000));
        Assert.Equal("199,000 ₫", CurrencyFormatter.FormatVnd(199000));
        Assert.Equal("1,000,000 ₫", CurrencyFormatter.FormatVnd(1000000));
    }

    [Fact]
    public void Area2_Presentation_CurrencyFormatter_Calculates_UnitPrice_And_Revenue()
    {
        Assert.Equal("49,000 ₫/credit", CurrencyFormatter.FormatUnitPrice(49000, 1));
        Assert.Equal("39,800 ₫/credit", CurrencyFormatter.FormatUnitPrice(199000, 5));
        Assert.Equal("20.2M ₫", CurrencyFormatter.FormatRevenue(20200000));
    }

    [Fact]
    public void Area2_Presentation_AiNarrativeFormatter_Escapes_Html_While_Formatting_Markdown()
    {
        var raw = "<script>alert(1)</script> **Notice**: Review *section 5* rules.";
        var safeHtml = AiNarrativeFormatter.ToSafeHtml(raw);

        Assert.DoesNotContain("<script>", safeHtml);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", safeHtml);
        Assert.Contains("<strong>Notice</strong>", safeHtml);
        Assert.Contains("<em>section 5</em>", safeHtml);
    }

    [Fact]
    public void Area2_Presentation_ExamIndexViewModel_Encapsulates_Catalog_And_ErrorMessage()
    {
        var catalog = new MockExamListViewDto(
            new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
            10,
            []);

        var vm = new ExamIndexViewModel(catalog, "Network error occurred");
        Assert.Equal(10, vm.Catalog.CreditBalance);
        Assert.Equal("Network error occurred", vm.ErrorMessage);
    }

    // =========================================================================
    // Area 3: DTO Separation & Contracts (>= 5 cases)
    // =========================================================================

    [Fact]
    public void Area3_Dto_SittingSyncRequestDto_Roundtrips_Json_Serialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = new SittingSyncRequestDto(
            Answers:
            [
                new BatchAnswerItemDto(101, 1001, false, null, null),
                new BatchAnswerItemDto(102, null, true, "Short response", null)
            ],
            LastAudioOffsetMs: 15400,
            CandidateNotesJson: "{\"note\":\"test\"}",
            LastQuestionId: 102);

        var json = JsonSerializer.Serialize(request, options);
        var deserialized = JsonSerializer.Deserialize<SittingSyncRequestDto>(json, options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Answers);
        Assert.Equal(2, deserialized.Answers.Count);
        Assert.Equal(101, deserialized.Answers[0].QuestionId);
        Assert.Equal(1001, deserialized.Answers[0].SelectedOptionId);
        Assert.False(deserialized.Answers[0].IsFlagged);
        Assert.Equal("Short response", deserialized.Answers[1].ResponseText);
        Assert.True(deserialized.Answers[1].IsFlagged);
        Assert.Equal(15400, deserialized.LastAudioOffsetMs);
        Assert.Equal(102, deserialized.LastQuestionId);
    }

    [Fact]
    public void Area3_Dto_SaveAnswerRequest_Preserves_Properties()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = new SaveAnswerRequest(
            SelectedOptionId: 1005,
            IsFlagged: true,
            ResponseText: "Sample text response",
            SelectedOptionIdsJson: "[1,2,3]");

        var json = JsonSerializer.Serialize(request, options);
        var deserialized = JsonSerializer.Deserialize<SaveAnswerRequest>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(1005, deserialized.SelectedOptionId);
        Assert.True(deserialized.IsFlagged);
        Assert.Equal("Sample text response", deserialized.ResponseText);
        Assert.Equal("[1,2,3]", deserialized.SelectedOptionIdsJson);
    }

    [Fact]
    public void Area3_Dto_SubmitAttemptDraftRequest_Roundtrips_Json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft = new SubmitAttemptDraftRequest(QuestionId: 501, ResponseText: "Draft answer text");

        var json = JsonSerializer.Serialize(draft, options);
        var deserialized = JsonSerializer.Deserialize<SubmitAttemptDraftRequest>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(501, deserialized.QuestionId);
        Assert.Equal("Draft answer text", deserialized.ResponseText);
    }

    [Fact]
    public void Area3_Dto_SaveQuestionRequest_Enforces_Record_Equality()
    {
        var req1 = new SaveQuestionRequest(Source: "manual");
        var req2 = new SaveQuestionRequest(Source: "manual");
        var req3 = new SaveQuestionRequest(Source: "incorrect");

        Assert.Equal(req1, req2);
        Assert.NotEqual(req1, req3);
        Assert.Equal("manual", req1.Source);
    }

    [Fact]
    public void Area3_Dto_AuthTokenResponse_Handles_Tokens_And_User()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var user = new AuthUserDto(42, "user@test.local", "Test User", "learner", true);
        var auth = new AuthTokenResponse("jwt-token-xyz", expiresAt, user);

        var json = JsonSerializer.Serialize(auth, options);
        var deserialized = JsonSerializer.Deserialize<AuthTokenResponse>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("jwt-token-xyz", deserialized.Token);
        Assert.Equal(42, deserialized.User.UserId);
        Assert.True(deserialized.User.EmailVerified);
    }

    // =========================================================================
    // Area 4: Client Interfaces & Error Pipelines (>= 5 cases)
    // =========================================================================

    [Fact]
    public void Area4_Client_ApiException_Preserves_Status_Code_Title_And_Detail()
    {
        var ex = new ApiException("ATTEMPT_NOT_FOUND", "Attempt was not found", 404, "ID 999 does not exist");

        Assert.Equal("ATTEMPT_NOT_FOUND", ex.Code);
        Assert.Equal("Attempt was not found", ex.Title);
        Assert.Equal(404, ex.Status);
        Assert.Equal("ID 999 does not exist", ex.Detail);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public void Area4_Client_ApiErrorCodes_Contain_Stable_Invariant_Values()
    {
        Assert.Equal("NOT_FOUND", ApiError.Codes.NotFound);
        Assert.Equal("VALIDATION", ApiError.Codes.Validation);
        Assert.Equal("ATTEMPT_NOT_FOUND", ApiError.Codes.AttemptNotFound);
        Assert.Equal("attempt_expired", ApiError.Codes.AttemptExpired);
        Assert.Equal("insufficient_credits", ApiError.Codes.InsufficientCredits);
        Assert.Equal("EXAM_NOT_FOUND", ApiError.Codes.ExamNotFound);
    }

    [Fact]
    public async Task Area4_Client_MockExamsClient_Exception_Handled_By_ExamsController_Instructions()
    {
        var api = new TestExamsClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.ExamNotFound, "Exam not found", 404)
        };

        var controller = CreateExamsController(api);
        var result = await controller.Instructions(999, System.Threading.CancellationToken.None);

        Assert.NotNull(result);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Index), redirect.ActionName);
    }

    [Fact]
    public void Area4_Client_CompanionApiClient_Implements_Domain_Client_Contracts()
    {
        // Verify type hierarchy and interface inheritance
        Assert.True(typeof(ICompanionApiClient).IsAssignableTo(typeof(IMockExamsClient)));
        Assert.True(typeof(ICompanionApiClient).IsAssignableTo(typeof(IPracticeClient)));
        Assert.True(typeof(ICompanionApiClient).IsAssignableTo(typeof(IPlacementClient)));
        Assert.True(typeof(ICompanionApiClient).IsAssignableTo(typeof(IWritingClient)));
        Assert.True(typeof(ICompanionApiClient).IsAssignableTo(typeof(ISpeakingClient)));
    }

    [Fact]
    public void Area4_Client_ApiError_Body_Record_Equality_And_Property_Integrity()
    {
        var err1 = new ApiError("VALIDATION", "Invalid payload", 400, "Field required");
        var err2 = new ApiError("VALIDATION", "Invalid payload", 400, "Field required");

        Assert.Equal(err1, err2);
        Assert.Equal("VALIDATION", err1.Code);
        Assert.Equal(400, err1.Status);
    }

    // Helper to instantiate ExamsController with test client
    private static ExamsController CreateExamsController(IMockExamsClient client)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal };
        var currentUser = new CurrentUser(new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext });

        var controller = new ExamsController(
            client,
            currentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExamsController>.Instance,
            new WebMVC.Formatters.DateTimeFormatter(new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext }),
            null)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                httpContext,
                new EmptyTempDataProvider())
        };
        return controller;
    }

    private sealed class EmptyTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public System.Collections.Generic.IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) =>
            new System.Collections.Generic.Dictionary<string, object>();
        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, System.Collections.Generic.IDictionary<string, object> values) { }
    }

    private sealed class TestExamsClient : IMockExamsClient
    {
        public Exception? ExceptionToThrow { get; set; }

        public Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult(new MockExamListViewDto(new CertificateOptionDto("TOEIC", "TOEIC"), 5, []));
        }

        public Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult(new MockExamInstructionsDto(examId, 1, "TOEIC", "Test Exam", null, 120, 200, 1, 5, null, []));
        }

        public Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
        public Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
