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
    public async Task TakeFragment_when_preflight_required_returns_conflict_carrying_attemptId()
    {
        // F7: the fragment redirect used to drop attemptId, so Preflight 404'd and the browser was
        // bounced out to /exams. The 409 body must name an attempt the Preflight action can resolve.
        var api = new TestApiClient
        {
            Preflight = new PreflightStatusDto(100, 1, "Placement", "TOEIC", "TOEIC", 45,
                true, true, false, false, null, []),
            PlacementTake = new PlacementTakeViewDto(42, 100, 1, "Placement", "TOEIC", PlacementStatusDto.InProgress,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 2700, 1, 0, 1,
                new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
                    "Stem", null, null, null, null, null, null, null, [], null, false), [], false)
        };

        var controller = CreateController(api);
        var result = await controller.TakeFragment(42, 2, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, status.StatusCode);
        var redirectUrl = status.Value?.GetType().GetProperty("redirectUrl")?.GetValue(status.Value) as string;
        Assert.NotNull(redirectUrl);
        Assert.Contains("attemptId=100", redirectUrl);
        Assert.Contains("placementId=42", redirectUrl);
        Assert.True(api.PreflightRequested);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Take_with_unverified_equipment_redirects_to_shared_preflight(bool audio, bool microphone)
    {
        var api = new TestApiClient
        {
            Preflight = new PreflightStatusDto(100, 1, "Placement", "TOEIC", "TOEIC", 45,
                true, audio, microphone, false, null, []),
            PlacementTake = new PlacementTakeViewDto(42, 100, 1, "Placement", "TOEIC", PlacementStatusDto.InProgress,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(45), 2700, 1, 0, 1,
                new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
                    "Stem", null, null, null, null, null, null, null, [], null, false), [], false)
        };

        var controller = CreateController(api);
        var result = await controller.Take(42, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ExamsController.Preflight), redirect.ActionName);
        Assert.Equal("Exams", redirect.ControllerName);
        Assert.Equal(100, redirect.RouteValues?["attemptId"]);
        Assert.Equal(42, redirect.RouteValues?["placementId"]);
        Assert.True(api.PreflightRequested);
    }

    [Fact]
    public async Task Take_with_active_placement_returns_view()
    {
        var q = new AttemptQuestionDto(
            10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening,
            "Look at the photo.", null, null, null, null, null, null, null,
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
        var q = new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, null, null, [], null, false);
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
        var q = new AttemptQuestionDto(10, 1, 20, 1, "P1", "Part 1", "listening", "Listening", ModalityDto.Listening, "Stem", null, null, null, null, null, null, null, [], null, false);
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
                new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), 30, 20, 750, "B1", "Intermediate",
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
        controller.HttpContext.Items[DateTimeFormatter.ZoneItemKey] = "Asia/Tokyo";
        var result = await controller.Result(42, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlacementResultViewModel>(viewResult.Model);
        Assert.Equal(42, model.Result.PlacementId);
        Assert.Equal(750, model.Result.TotalScore);
        Assert.Equal("B1", model.Result.ResultBandCode);
        Assert.Equal("Reading", model.Result.WeakerSkillName);
        Assert.Equal("26 October 2026", model.TakenAtDisplay);
        Assert.Equal(940, model.MaximumScore);
        Assert.Equal(380, model.Listening?.ScaledScore);
        Assert.Equal(370, model.Reading?.ScaledScore);
        Assert.Equal("cefr__band is-current", model.Bands[1].CssClass);
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

        var controller = new PlacementController(api, currentUser, NullLogger<PlacementController>.Instance, new WebMVC.Formatters.DateTimeFormatter(new HttpContextAccessor { HttpContext = httpContext }))
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
        public string? Action(UrlActionContext actionContext)
        {
            var values = actionContext.Values;
            if (values is null) return $"/{actionContext.Action?.ToLower()}";
            var query = string.Join("&", values.GetType().GetProperties()
                .Select(property => $"{property.Name}={property.GetValue(values)}"));
            return $"/{actionContext.Action?.ToLower()}?{query}";
        }
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
        public PreflightStatusDto? Preflight { get; set; }
        public bool PreflightRequested { get; private set; }

        public Task<PreflightStatusDto> GetPlacementPreflightStatusAsync(int placementId, CancellationToken cancellationToken = default)
        {
            PreflightRequested = true;
            return ExceptionToThrow != null ? Task.FromException<PreflightStatusDto>(ExceptionToThrow)
                : Task.FromResult(Preflight ?? new PreflightStatusDto(100, 1, "Placement", "TOEIC", "TOEIC", 45,
                    false, false, false, true, DateTimeOffset.UtcNow, []));
        }
        public SaveAnswerResultDto? SaveAnswerResult { get; set; }
        public PlacementResultViewDto? PlacementResult { get; set; }
        public TargetBandViewDto? TargetBandView { get; set; }
        public SetTargetBandResultDto? SetTargetBandResult { get; set; }
        public SubmitPlacementTestRequest? LastSubmitRequest { get; set; }

        public Task<PlacementCatalogViewDto> GetPlacementCatalogAsync(CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementCatalogViewDto>(ExceptionToThrow) : Task.FromResult(PlacementCatalog!);

        public Task<PlacementTestInfoDto> StartPlacementTestAsync(StartPlacementTestRequest request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementTestInfoDto>(ExceptionToThrow) : Task.FromResult(StartPlacementResponse!);

        public Task<PlacementTakeViewDto> GetPlacementTakeAsync(int placementId, int? questionDisplayOrder = null, CancellationToken cancellationToken = default)
        {
            if (Preflight is { RequiresPreflight: true, IsVerified: false })
            {
                return Task.FromException<PlacementTakeViewDto>(new ApiException(ApiError.Codes.PreflightRequired, "Preflight required", 400));
            }
            return ExceptionToThrow != null ? Task.FromException<PlacementTakeViewDto>(ExceptionToThrow) : Task.FromResult(PlacementTake!);
        }

        public Task<SavePlacementDraftResponseDto> SavePlacementDraftAsync(int placementId, SavePlacementDraftRequestDto request, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SavePlacementDraftResponseDto>(ExceptionToThrow) : Task.FromResult(new SavePlacementDraftResponseDto(placementId, request.QuestionId, DateTimeOffset.UtcNow, request.ResponseText?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0));

        public Task<PlacementGradingStatusDto> GetPlacementGradingStatusAsync(int placementId, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<PlacementGradingStatusDto>(ExceptionToThrow) : Task.FromResult(new PlacementGradingStatusDto(placementId, 1, true, 0, 0, 0, 800, "B2", "Vantage", Array.Empty<PlacementSkillScoreInfoDto>(), "Complete"));

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
