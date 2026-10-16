using System.Security.Claims;
using System.Text.Json;
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

public sealed class PlacementControllerTests
{
    [Fact]
    public async Task Index_returns_view_with_catalog()
    {
        var api = new TestApiClient
        {
            PlacementCatalog = new PlacementCatalogViewDto(
                new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
                "B1",
                "Intermediate",
                750,
                [
                    new PlacementCatalogItemDto(1, 1, "TOEIC", "TOEIC Placement Test — Standard", "Full diagnostic", 45, 20, true, null),
                    new PlacementCatalogItemDto(2, 1, "TOEIC", "TOEIC Placement Test — Quick", "Fast diagnostic", 20, 10, false, null)
                ],
                null,
                null)
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlacementSelectViewModel>(viewResult.Model);
        Assert.Equal(2, model.Catalog.Exams.Count);
        Assert.Equal("B1", model.Catalog.CurrentBandCode);
        Assert.Equal(750, model.Catalog.CurrentTotalScore);
    }

    [Fact]
    public async Task Index_when_api_exception_redirects_to_Dashboard()
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
    public async Task Start_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            StartPlacementResponse = new PlacementTestInfoDto(
                42, 100, 1, PlacementStatusDto.InProgress, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 2700, false)
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Start_when_exam_not_found_redirects_to_Index_with_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.ExamNotFound, "Exam not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Start(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Start_when_api_exception_redirects_to_Index_with_error_message()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("ERROR", "Something broke", 500, "Something broke")
        };

        var controller = CreateController(api);
        var result = await controller.Start(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.Equal("Something broke", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Take_with_active_placement_returns_view()
    {
        var q = new AttemptQuestionDto(
            10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
            "Look at the photo.", null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "Statement A"), new AttemptOptionDto(2, "B", "Statement B")],
            null, false);

        var take = new PlacementTakeViewDto(
            42, 100, 1, "TOEIC Placement Test — Standard", "TOEIC", PlacementStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 2700,
            20, 5, 1, q,
            [new NavigatorItemDto(10, 1, 1, "P1", ModalityDto.Listening, false, false, true)],
            false);

        var api = new TestApiClient { PlacementTake = take };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlacementTakeViewModel>(viewResult.Model);
        Assert.Equal(42, model.Take.PlacementId);
        Assert.Equal(10, model.Take.CurrentQuestion.QuestionId);
        Assert.Equal("TOEIC Placement Test — Standard", model.Take.ExamName);
    }

    [Fact]
    public async Task Take_when_completed_redirects_to_Result()
    {
        var q = new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, [], null, false);
        var take = new PlacementTakeViewDto(
            42, 100, 1, "TOEIC Placement Test — Standard", "TOEIC", PlacementStatusDto.Done,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 0,
            20, 20, 0, q, [], false);

        var api = new TestApiClient { PlacementTake = take };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Result), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Take_when_abandoned_redirects_to_Index()
    {
        var q = new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, [], null, false);
        var take = new PlacementTakeViewDto(
            42, 100, 1, "TOEIC Placement Test — Standard", "TOEIC", PlacementStatusDto.Abandoned,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 0,
            20, 20, 0, q, [], false);

        var api = new TestApiClient { PlacementTake = take };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.Equal("This placement test has been abandoned.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Take_when_expired_redirects_to_Result()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementExpired, "Placement expired", 400)
        };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Result), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Take_when_not_found_returns_PlacementNotFound_404()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementNotFound, "Placement not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Take(999, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("PlacementNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Take_when_unexpected_api_exception_redirects_to_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "Unexpected failure", 500)
        };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task SaveAnswer_returns_json_result()
    {
        var api = new TestApiClient
        {
            SaveAnswerResult = new SaveAnswerResultDto(10, 2, true, 1800, false)
        };

        var controller = CreateController(api);
        var form = new SaveAnswerFormModel(2, true);
        var result = await controller.SaveAnswer(42, 10, form, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var element = JsonSerializer.SerializeToElement(json.Value);
        Assert.Equal(10, element.GetProperty("questionId").GetInt32());
        Assert.Equal(2, element.GetProperty("selectedOptionId").GetInt32());
        Assert.True(element.GetProperty("isFlagged").GetBoolean());
        Assert.Equal(1800, element.GetProperty("remainingSeconds").GetInt32());
        Assert.False(element.GetProperty("isAutoSubmitted").GetBoolean());
    }

    [Fact]
    public async Task SaveAnswer_when_expired_returns_json_with_auto_submitted_and_redirect_url()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementExpired, "Expired", 400)
        };

        var controller = CreateController(api);
        var form = new SaveAnswerFormModel(2, false);
        var result = await controller.SaveAnswer(42, 10, form, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var element = JsonSerializer.SerializeToElement(json.Value);
        Assert.True(element.GetProperty("isAutoSubmitted").GetBoolean());
        Assert.NotNull(element.GetProperty("redirectUrl").GetString());
    }

    [Fact]
    public async Task SaveAnswer_when_error_returns_status_code()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementNotActive, "Placement is not active", 400)
        };

        var controller = CreateController(api);
        var form = new SaveAnswerFormModel(999, false);
        var result = await controller.SaveAnswer(42, 10, form, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusResult.StatusCode);
    }

    [Fact]
    public async Task Submit_redirects_to_Result()
    {
        var api = new TestApiClient();

        var controller = CreateController(api);
        var result = await controller.Submit(42, isAutoSubmit: false, cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Result), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Submit_with_isAutoSubmit_true_passes_flag_to_api()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Submit(42, isAutoSubmit: true, cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Result), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
        Assert.NotNull(api.LastSubmitRequest);
        Assert.True(api.LastSubmitRequest.IsAutoSubmit);
    }

    [Fact]
    public async Task Submit_when_expired_redirects_to_Result()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementExpired, "Expired", 400)
        };

        var controller = CreateController(api);
        var result = await controller.Submit(42, isAutoSubmit: false, cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Result), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Submit_when_unexpected_error_redirects_to_Take_with_error_message()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "Submit failed", 500, "Submit failed")
        };

        var controller = CreateController(api);
        var result = await controller.Submit(42, isAutoSubmit: false, cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
        Assert.Equal("Submit failed", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Result_returns_view_with_result_model()
    {
        var api = new TestApiClient
        {
            PlacementResult = new PlacementResultViewDto(
                42, 100, 1, "TOEIC", "TOEIC Placement Test — Standard",
                DateTimeOffset.UtcNow, 30, 20, 750, "B1", "Intermediate",
                [
                    new CefrBandInfoDto("A2", "Elementary", 225, 545, true, false, false),
                    new CefrBandInfoDto("B1", "Intermediate", 550, 780, true, true, false),
                    new CefrBandInfoDto("B2", "Upper Intermediate", 785, 940, false, false, true)
                ],
                [
                    new PlacementSkillScoreInfoDto(1, "LIST", "Listening", 16, 20, 380, "B1"),
                    new PlacementSkillScoreInfoDto(2, "READ", "Reading", 14, 20, 370, "B1")
                ],
                "Reading",
                "Your Reading accuracy (70%) is lower than Listening (80%).")
        };

        var controller = CreateController(api);
        var result = await controller.Result(42, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlacementResultViewModel>(viewResult.Model);
        Assert.Equal(42, model.Result.PlacementId);
        Assert.Equal(750, model.Result.TotalScore);
        Assert.Equal("B1", model.Result.ResultBandCode);
        Assert.Equal("Reading", model.Result.WeakerSkillName);
    }

    [Fact]
    public async Task Result_when_placement_not_finished_redirects_to_Take()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementNotFinished, "Placement not finished", 400)
        };

        var controller = CreateController(api);
        var result = await controller.Result(42, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Take), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
    }

    [Fact]
    public async Task Result_when_not_found_returns_PlacementNotFound_404()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.PlacementNotFound, "Placement not found", 404)
        };

        var controller = CreateController(api);
        var result = await controller.Result(999, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("PlacementNotFound", viewResult.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Result_when_unexpected_error_redirects_to_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "Database connection lost", 500)
        };

        var controller = CreateController(api);
        var result = await controller.Result(42, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task TargetBand_GET_returns_view_with_target_bands()
    {
        var api = new TestApiClient
        {
            TargetBandView = new TargetBandViewDto(
                new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                "B1", 750, "Intermediate", "B2",
                [
                    new CefrBandInfoDto("A2", "Elementary", 225, 545, true, false, false),
                    new CefrBandInfoDto("B1", "Intermediate", 550, 780, true, true, false),
                    new CefrBandInfoDto("B2", "Upper Intermediate", 785, 940, false, false, true)
                ],
                [
                    new TargetBandOptionDto("A2", "Elementary", 225, 545, "Waystage", false, false),
                    new TargetBandOptionDto("B1", "Intermediate", 550, 780, "Threshold", true, false),
                    new TargetBandOptionDto("B2", "Upper Intermediate", 785, 940, "Vantage", false, true)
                ])
        };

        var controller = CreateController(api);
        var result = await controller.TargetBand(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TargetBandViewModel>(viewResult.Model);
        Assert.Equal("B2", model.View.TargetBandCode);
        Assert.Equal(750, model.View.CurrentTotalScore);
        Assert.Equal(3, model.View.AvailableBands.Count);
    }

    [Fact]
    public async Task TargetBand_GET_when_error_redirects_to_Index()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("FAIL", "Server error", 500)
        };

        var controller = CreateController(api);
        var result = await controller.TargetBand(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task TargetBand_POST_when_target_band_code_empty_redirects_to_TargetBand_with_error()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var form = new SetTargetBandFormModel { TargetBandCode = "" };
        var result = await controller.TargetBand(form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.TargetBand), redirect.ActionName);
        Assert.Equal("Please select a target level.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task TargetBand_POST_saves_and_redirects_with_success_message()
    {
        var api = new TestApiClient
        {
            SetTargetBandResult = new SetTargetBandResultDto(7, 1, "B2", DateTimeOffset.UtcNow)
        };

        var controller = CreateController(api);
        var form = new SetTargetBandFormModel { TargetBandCode = "B2" };
        var result = await controller.TargetBand(form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.TargetBand), redirect.ActionName);
        Assert.Equal("Your target level has been saved.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task TargetBand_POST_when_api_exception_redirects_with_error_message()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.TargetBandInvalid, "Target band code is invalid", 400, "Target band code is invalid")
        };

        var controller = CreateController(api);
        var form = new SetTargetBandFormModel { TargetBandCode = "INVALID" };
        var result = await controller.TargetBand(form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PlacementController.TargetBand), redirect.ActionName);
        Assert.Equal("Target band code is invalid", controller.TempData["ErrorMessage"]);
    }

    private static PlacementController CreateController(TestApiClient api)
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

        var controller = new PlacementController(api, currentUser, NullLogger<PlacementController>.Instance)
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
        public string? Action(UrlActionContext actionContext) => $"/placement/{actionContext.Action?.ToLower()}/{actionContext.Values?.GetType().GetProperty("placementId")?.GetValue(actionContext.Values)}";
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

    private sealed class TestApiClient : IPlacementClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public PlacementCatalogViewDto? PlacementCatalog { get; set; }
        public PlacementTestInfoDto? StartPlacementResponse { get; set; }
        public PlacementTakeViewDto? PlacementTake { get; set; }
        public SaveAnswerResultDto? SaveAnswerResult { get; set; }
        public PlacementResultViewDto? PlacementResult { get; set; }
        public TargetBandViewDto? TargetBandView { get; set; }
        public SetTargetBandResultDto? SetTargetBandResult { get; set; }
        public SubmitPlacementTestRequest? LastSubmitRequest { get; set; }

        public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementCatalogViewDto>(ExceptionToThrow) : Task.FromResult(PlacementCatalog!);

        public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementTestInfoDto>(ExceptionToThrow) : Task.FromResult(StartPlacementResponse!);

        public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder = null, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementTakeViewDto>(ExceptionToThrow) : Task.FromResult(PlacementTake!);

        public Task<SaveAnswerResultDto> SavePlacementAnswerAsync(int placementId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SaveAnswerResultDto>(ExceptionToThrow) : Task.FromResult(SaveAnswerResult!);

        public Task<PlacementResultViewDto> SubmitPlacementTestAsync(int placementId, SubmitPlacementTestRequest request, CancellationToken cancellationToken = default)
        {
            LastSubmitRequest = request;
            return ExceptionToThrow != null ? Task.FromException<PlacementResultViewDto>(ExceptionToThrow) : Task.FromResult(PlacementResult!);
        }

        public Task<PlacementResultViewDto> GetPlacementResultAsync(int placementId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementResultViewDto>(ExceptionToThrow) : Task.FromResult(PlacementResult!);

        public Task<TargetBandViewDto> GetTargetBandViewAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<TargetBandViewDto>(ExceptionToThrow) : Task.FromResult(TargetBandView!);

        public Task<SetTargetBandResultDto> SetTargetBandAsync(SetTargetBandRequest request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SetTargetBandResultDto>(ExceptionToThrow) : Task.FromResult(SetTargetBandResult!);
    }
}
