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

public sealed class LearningPathControllerTests
{
    [Fact]
    public async Task Index_WhenNoActivePath_ReturnsViewWithHasActivePathFalse()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: false,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: null,
                FromBandCode: null,
                ToBandCode: null,
                FromBandName: null,
                ToBandName: null,
                CurrentBandCode: null,
                CurrentScore: null,
                TargetBandCode: null,
                TargetScore: null,
                Status: null,
                StartedAt: null,
                IsPremium: false,
                TotalModules: 0,
                CompletedModules: 0,
                TotalEstimatedMinutes: 0,
                RemainingMinutes: 0,
                CefrBands: [],
                Modules: [],
                NextModuleId: null)
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathIndexViewModel>(viewResult.Model);
        Assert.False(model.Summary.HasActivePath);
        Assert.Empty(model.Summary.Modules);
    }

    [Fact]
    public async Task Index_WhenActivePath_ReturnsViewWithSummary()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: false,
                TotalModules: 2,
                CompletedModules: 1,
                TotalEstimatedMinutes: 45,
                RemainingMinutes: 20,
                CefrBands: [],
                Modules:
                [
                    new LearningPathModuleSummaryDto(1, 1, "Module 1", "Group 1", "Desc 1", 25, 1, "READ", "Reading", ModalityDto.Reading, "done", false, false, 20, 20, 1, 1, null),
                    new LearningPathModuleSummaryDto(2, 1, "Module 2", "Group 1", "Desc 2", 20, 2, "READ", "Reading", ModalityDto.Reading, "in_progress", false, true, 25, 10, 1, 0, 5)
                ],
                NextModuleId: 2)
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathIndexViewModel>(viewResult.Model);
        Assert.True(model.Summary.HasActivePath);
        Assert.Equal(2, model.Summary.TotalModules);
        Assert.Equal(1, model.Summary.CompletedModules);
    }

    [Fact]
    public async Task Index_WhenApiException_RedirectsToDashboard()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "API unavailable", 500)
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Generate_CallsApiAndRedirectsToIndex()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "A2",
                ToBandName: "B2",
                CurrentBandCode: "A2",
                CurrentScore: null,
                TargetBandCode: "B2",
                TargetScore: null,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: false,
                TotalModules: 1,
                CompletedModules: 0,
                TotalEstimatedMinutes: 20,
                RemainingMinutes: 20,
                CefrBands: [],
                Modules: [],
                NextModuleId: null)
        };

        var controller = CreateController(api);
        var result = await controller.Generate("B2", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(LearningPathController.Index), redirect.ActionName);
        Assert.Equal("B2", api.LastGenerateRequest?.TargetBandCode);
    }

    [Fact]
    public async Task Generate_WhenAlreadyActive_RedirectsToIndexWithoutError()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.LearningPathAlreadyActive, "Path already active", 409)
        };

        var controller = CreateController(api);
        var result = await controller.Generate(null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(LearningPathController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task ModuleDetail_ReturnsViewWithModuleDetail()
    {
        var api = new TestApiClient
        {
            LearningPathModuleDetail = new LearningPathModuleDetailDto(
                LearnerPathId: 10,
                ModuleId: 2,
                Title: "Module 2: Present Continuous",
                GroupName: "Grammar foundations",
                Description: "Module description",
                EstimatedMinutes: 20,
                DisplayOrder: 2,
                SkillCode: "READ",
                SkillName: "Reading",
                Modality: ModalityDto.Reading,
                Status: "in_progress",
                IsLocked: false,
                ModuleIndex: 2,
                TotalModules: 5,
                FromBandCode: "A2",
                ToBandCode: "B2",
                AnsweredQuestions: 15,
                TotalQuestions: 25,
                AccuracyPercentage: 73,
                CorrectCount: 11,
                TotalAnswered: 15,
                TimeSpentMinutes: 12,
                EstimatedMinutesRemaining: 8,
                IsPremium: false,
                Topics:
                [
                    new ModuleTopicDto(1, "Topic 1", 8, "done")
                ],
                Steps:
                [
                    new ModuleStepDto(5, 2, PathModuleItemTypeDto.PracticeSection, "Practice: Module 2", LearnerPathStepStatusDto.InProgress, null, 25, false, true, DateTimeOffset.UtcNow, null, 1, null)
                ],
                ActiveStepId: 5)
        };

        var controller = CreateController(api);
        var result = await controller.ModuleDetail(2, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathModuleDetailViewModel>(viewResult.Model);
        Assert.Equal(2, model.Detail.ModuleId);
        Assert.Equal("Module 2: Present Continuous", model.Detail.Title);
        Assert.Equal(15, model.Detail.AnsweredQuestions);
        Assert.Equal(73, model.Detail.AccuracyPercentage);
    }

    [Fact]
    public async Task ModuleDetail_WhenNotFound_RedirectsToIndex()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.LearningPathModuleNotFound, "Module not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.ModuleDetail(99, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(LearningPathController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task StartStep_CallsApiAndRedirectsToTargetUrl()
    {
        var api = new TestApiClient
        {
            StartStepResult = new StartStepResultDto(
                StepId: 5,
                ItemType: PathModuleItemTypeDto.PracticeSection,
                RedirectUrl: "/practice/42",
                SessionId: 42,
                AttemptId: null)
        };

        var controller = CreateController(api);
        var result = await controller.StartStep(5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/practice/42", redirect.Url);
    }

    [Fact]
    public async Task StartStep_WhenStepLocked_RedirectsToIndexWithErrorMessage()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.LearningPathStepLocked, "Step is locked", 400)
        };

        var controller = CreateController(api);
        var result = await controller.StartStep(6, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(LearningPathController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Reset_CallsApiAndRedirectsToIndex()
    {
        var api = new TestApiClient
        {
            ResetResult = new ResetLearningPathResultDto(true)
        };

        var controller = CreateController(api);
        var result = await controller.Reset(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(LearningPathController.Index), redirect.ActionName);
    }

    private static LearningPathController CreateController(TestApiClient api)
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

        return new LearningPathController(api, currentUser, NullLogger<LearningPathController>.Instance)
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
        public LearningPathSummaryDto? LearningPathSummary { get; set; }
        public LearningPathModuleDetailDto? LearningPathModuleDetail { get; set; }
        public StartStepResultDto? StartStepResult { get; set; }
        public ResetLearningPathResultDto? ResetResult { get; set; }
        public GenerateLearningPathRequest? LastGenerateRequest { get; set; }

        public Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<LearningPathSummaryDto>(ExceptionToThrow) : Task.FromResult(LearningPathSummary!);

        public Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default)
        {
            LastGenerateRequest = request;
            return ExceptionToThrow != null ? Task.FromException<LearningPathSummaryDto>(ExceptionToThrow) : Task.FromResult(LearningPathSummary!);
        }

        public Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<LearningPathModuleDetailDto>(ExceptionToThrow) : Task.FromResult(LearningPathModuleDetail!);

        public Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<StartStepResultDto>(ExceptionToThrow) : Task.FromResult(StartStepResult!);

        public Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<ResetLearningPathResultDto>(ExceptionToThrow) : Task.FromResult(ResetResult!);

        public WalletDto? Wallet { get; set; }
        public AiInsightDto? PathInsight { get; set; }
        public bool UpgradeCalled { get; set; }
        public bool RetryPathInsightCalled { get; set; }

        public Task<WalletDto> GetWalletAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<WalletDto>(ExceptionToThrow) : Task.FromResult(Wallet ?? new WalletDto(10, 10, 0, 100000m, 1, [], []));

        public Task<LearningPathSummaryDto> UpgradeLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default)
        {
            UpgradeCalled = true;
            return ExceptionToThrow != null ? Task.FromException<LearningPathSummaryDto>(ExceptionToThrow) : Task.FromResult(LearningPathSummary!);
        }

        public Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AiInsightDto>(ExceptionToThrow) : Task.FromResult(PathInsight ?? new AiInsightDto(
                InsightId: 1,
                UserId: 1,
                CertId: 1,
                Scope: AiInsightScopeDto.Path,
                PracticeSessionId: null,
                AttemptId: null,
                Narrative: "Sample path insight narrative",
                Trend: AiInsightTrendDto.Improving,
                Status: AiInsightStatusDto.Done,
                FailureReason: null,
                Model: "fake-model",
                PromptTokens: 100,
                CompletionTokens: 80,
                SourceFingerprint: new string('a', 64),
                CreatedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                Recommendations: []));

        public Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default)
        {
            RetryPathInsightCalled = true;
            return ExceptionToThrow != null ? Task.FromException(ExceptionToThrow) : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Index_WhenActivePath_PopulatesInsightPanel()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: false,
                TotalModules: 2,
                CompletedModules: 1,
                TotalEstimatedMinutes: 45,
                RemainingMinutes: 20,
                CefrBands: [],
                Modules: [],
                NextModuleId: 2)
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathIndexViewModel>(viewResult.Model);
        Assert.NotNull(model.InsightPanel);
        Assert.Equal("path", model.InsightPanel.Scope);
        Assert.Equal(10, model.InsightPanel.ActivityId);
        Assert.NotNull(model.InsightPanel.Insight);
    }

    [Fact]
    public async Task Upgrade_WhenBalanceLessThanFive_RendersPaywall()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: false,
                TotalModules: 14,
                CompletedModules: 1,
                TotalEstimatedMinutes: 200,
                RemainingMinutes: 180,
                CefrBands: [],
                Modules: [],
                NextModuleId: 2),
            Wallet = new WalletDto(3, 3, 0, 30000m, 1, [], [])
        };

        var controller = CreateController(api);
        var result = await controller.Upgrade(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathUpgradeViewModel>(viewResult.Model);
        Assert.False(model.CanUpgrade);
        Assert.Equal(3, model.Balance);
        Assert.Equal(5, model.UpgradeCost);
        Assert.Equal(2, model.ShortBy);
    }

    [Fact]
    public async Task Upgrade_WhenBalanceAtLeastFive_RendersConfirmation()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: false,
                TotalModules: 14,
                CompletedModules: 1,
                TotalEstimatedMinutes: 200,
                RemainingMinutes: 180,
                CefrBands: [],
                Modules: [],
                NextModuleId: 2),
            Wallet = new WalletDto(8, 8, 0, 80000m, 1, [], [])
        };

        var controller = CreateController(api);
        var result = await controller.Upgrade(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathUpgradeViewModel>(viewResult.Model);
        Assert.True(model.CanUpgrade);
        Assert.Equal(8, model.Balance);
        Assert.Equal(5, model.UpgradeCost);
        Assert.Equal(0, model.ShortBy);
    }

    [Fact]
    public async Task UpgradeConfirmed_WhenSuccessful_RedirectsToIndexWithSuccessMessage()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: true,
                TotalModules: 14,
                CompletedModules: 1,
                TotalEstimatedMinutes: 200,
                RemainingMinutes: 180,
                CefrBands: [],
                Modules: [],
                NextModuleId: 2)
        };

        var controller = CreateController(api);
        var result = await controller.UpgradeConfirmed(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.True(api.UpgradeCalled);
        Assert.NotNull(controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task UpgradeConfirmed_WhenInsufficientCredits_RedirectsToUpgradeWithErrorMessage()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "Insufficient credits", 400)
        };

        var controller = CreateController(api);
        var result = await controller.UpgradeConfirmed(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Upgrade", redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task GetPathInsight_WhenSuccessful_ReturnsOk()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.GetPathInsight(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<AiInsightDto>(ok.Value);
        Assert.Equal(AiInsightScopeDto.Path, dto.Scope);
    }

    [Fact]
    public async Task RetryPathInsight_WhenSuccessful_ReturnsOk()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.RetryPathInsight(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(api.RetryPathInsightCalled);
    }

    [Fact]
    public async Task Upgrade_WhenNoActivePath_RedirectsToIndex()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: false,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC"),
                LearnerPathId: null,
                FromBandCode: null,
                ToBandCode: null,
                FromBandName: null,
                ToBandName: null,
                CurrentBandCode: null,
                CurrentScore: null,
                TargetBandCode: null,
                TargetScore: null,
                Status: null,
                StartedAt: null,
                IsPremium: false,
                TotalModules: 0,
                CompletedModules: 0,
                TotalEstimatedMinutes: 0,
                RemainingMinutes: 0,
                CefrBands: [],
                Modules: [],
                NextModuleId: null)
        };

        var controller = CreateController(api);
        var result = await controller.Upgrade(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Upgrade_WhenApiException_RedirectsToIndexWithErrorMessage()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("api_error", "Failed to fetch wallet", 500)
        };

        var controller = CreateController(api);
        var result = await controller.Upgrade(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task UpgradeConfirmed_WhenGenericApiException_RedirectsToUpgradeWithErrorMessage()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("general_error", "Something went wrong", 500)
        };

        var controller = CreateController(api);
        var result = await controller.UpgradeConfirmed(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Upgrade", redirect.ActionName);
        Assert.Equal("Something went wrong", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task GetPathInsight_WhenApiException_ReturnsStatusCode()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("not_found", "Insight not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.GetPathInsight(CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public async Task RetryPathInsight_WhenApiException_ReturnsStatusCode()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("not_found", "Insight not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.RetryPathInsight(CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public async Task Index_WhenSuccessMessageInTempData_PassesSuccessMessageToModel()
    {
        var api = new TestApiClient
        {
            LearningPathSummary = new LearningPathSummaryDto(
                HasActivePath: true,
                ActiveCert: new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                LearnerPathId: 10,
                FromBandCode: "A2",
                ToBandCode: "B2",
                FromBandName: "Elementary",
                ToBandName: "Upper intermediate",
                CurrentBandCode: "A2",
                CurrentScore: 385,
                TargetBandCode: "B2",
                TargetScore: 785,
                Status: LearnerPathStatusDto.Active,
                StartedAt: DateTimeOffset.UtcNow,
                IsPremium: true,
                TotalModules: 2,
                CompletedModules: 1,
                TotalEstimatedMinutes: 45,
                RemainingMinutes: 20,
                CefrBands: [],
                Modules: [],
                NextModuleId: 2)
        };
        var controller = CreateController(api);
        controller.TempData["SuccessMessage"] = "Successfully upgraded to premium path!";

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearningPathIndexViewModel>(viewResult.Model);
        Assert.Equal("Successfully upgraded to premium path!", model.SuccessMessage);
    }
}
