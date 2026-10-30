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
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminCertificatesControllerTests
{
    private static AdminCertificatesController CreateController(IAdminCertificatesClient api, IAdminScoreConversionClient? scoreConversionClient = null)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(1, "admin@toeic.local", Roles.Admin, "Admin"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminCertificatesController(
            currentUser,
            api,
            NullLogger<AdminCertificatesController>.Instance,
            scoreConversionClient)
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

    private sealed class TestAdminCertificatesClient : IAdminCertificatesClient
    {
        public List<AdminCertificateSummaryDto> AdminCertificates { get; set; } =
        [
            new(1, "TOEIC", "TOEIC L&R", "Standard workplace test", "sum", "toeic_lr_std", true, 2, 7, 3),
            new(2, "IELTS", "IELTS Academic", "Academic test", "avg_half_step", "ielts_academic", true, 4, 5, 2)
        ];

        public List<CertificateBlueprintPresetDto> Presets { get; set; } =
        [
            new("ielts_academic", "IELTS Academic", "IELTS preset", "avg_half_step",
            [
                new("LIST", "Listening", ModalityDto.Listening, 1, 30,
                [
                    new("REC", "Recordings", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"AUDIO_PLUS_CHECK\"}", LayoutTypeDto.SplitHorizontal, 4, 1, 1, false, false, true, false, 0, 40, null, "{\"highlighter\":true}")
                ])
            ]),
            new("toeic_sw", "TOEIC Speaking & Writing", "TOEIC SW preset", "independent",
            [
                new("SPEA", "Speaking", ModalityDto.Speaking, 1, 20,
                [
                    new("S1", "Read aloud", 1, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 2, "[\"free_response\"]", null)
                ])
            ])
        ];

        public CertificateBlueprintDetailDto? BlueprintToReturn { get; set; }
        public SaveCertificateWizardRequest? LastSavedRequest { get; private set; }
        public bool ThrowOnSave { get; set; }
        public HttpRequestException? LoadException { get; set; }

        public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default) =>
            LoadException is not null
                ? Task.FromException<IReadOnlyList<AdminCertificateSummaryDto>>(LoadException)
                : Task.FromResult<IReadOnlyList<AdminCertificateSummaryDto>>(AdminCertificates);

        public Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
            LoadException is not null
                ? Task.FromException<IReadOnlyList<CertificateBlueprintPresetDto>>(LoadException)
                : Task.FromResult<IReadOnlyList<CertificateBlueprintPresetDto>>(Presets);

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default) =>
            LoadException is not null
                ? Task.FromException<CertificateBlueprintDetailDto?>(LoadException)
                : Task.FromResult(BlueprintToReturn);

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(BlueprintToReturn);

        public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new ApiException("validation_error", "Validation failed", 400, "Skill code is missing");
            }
            LastSavedRequest = request;
            return Task.FromResult(new SaveCertificateWizardResultDto(request.CertId ?? 99, request.Code, request.Name, request.Skills.Count, request.Skills.Sum(s => s.Sections.Count)));
        }
    }

    /// <summary>The two ways a load fails (#136): the API answers with an error status
    /// (ApiException), or it cannot be reached at all (a bare HttpRequestException).</summary>
    private static HttpRequestException LoadFailure(bool unreachable) => unreachable
        ? new HttpRequestException("No connection could be made because the target machine actively refused it.")
        : new ApiException("upstream_unavailable", "Service unavailable", 503);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_when_api_throws_renders_error_banner_instead_of_crashing(bool unreachable)
    {
        var api = new TestAdminCertificatesClient
        {
            LoadException = LoadFailure(unreachable),
        };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Certificates.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<AdminCertificateListViewModel>(viewResult.Model);
        Assert.Empty(vm.Certificates);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wizard_when_presets_fail_renders_degraded_page_with_error(bool unreachable)
    {
        var api = new TestAdminCertificatesClient
        {
            LoadException = LoadFailure(unreachable),
        };
        var controller = CreateController(api);

        var result = await controller.Wizard(null, null, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/CertificateWizard.cshtml", viewResult.ViewName);
        var page = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model);
        Assert.Empty(page.Form.Presets);
        Assert.False(string.IsNullOrWhiteSpace(page.Form.ErrorMessage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wizard_when_blueprint_fails_keeps_presets_and_surfaces_error(bool unreachable)
    {
        // Only the blueprint-for-edit lookup fails; the preset catalogue loads normally.
        var failure = unreachable
            ? LoadFailure(unreachable)
            : new ApiException("exam_not_found", "Certificate not found", 404);
        var controller = CreateController(new BlueprintFailingClient(new TestAdminCertificatesClient(), failure));

        var result = await controller.Wizard(null, 7, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var page = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model);
        Assert.NotEmpty(page.Form.Presets);
        Assert.False(string.IsNullOrWhiteSpace(page.Form.ErrorMessage));
    }

    /// <summary>Presets load normally; only the blueprint-for-edit lookup fails.</summary>
    private sealed class BlueprintFailingClient(TestAdminCertificatesClient inner, HttpRequestException failure) : IAdminCertificatesClient
    {
        public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default) =>
            inner.GetAdminCertificatesAsync(cancellationToken);

        public Task<IReadOnlyList<CertificateBlueprintPresetDto>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
            inner.GetPresetsAsync(cancellationToken);

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default) =>
            Task.FromException<CertificateBlueprintDetailDto?>(failure);

        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            inner.GetCertificateBlueprintByCodeAsync(code, cancellationToken);

        public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveCertificateWizardAsync(request, cancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveWizard_when_presets_fail_redisplays_with_error_and_does_not_save(bool unreachable)
    {
        var api = new TestAdminCertificatesClient
        {
            LoadException = LoadFailure(unreachable),
        };
        var controller = CreateController(api);

        var result = await controller.SaveWizard(new CertificateWizardViewModel { Code = "IELTS", Name = "IELTS" }, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/CertificateWizard.cshtml", viewResult.ViewName);
        var page = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model);
        Assert.False(string.IsNullOrWhiteSpace(page.Form.ErrorMessage));
        Assert.Null(api.LastSavedRequest);
    }

    [Fact]
    public async Task Index_Returns_View_With_Certificates_List()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Certificates.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<AdminCertificateListViewModel>(viewResult.Model);
        Assert.Equal(2, vm.Certificates.Count);
        Assert.Equal("TOEIC", vm.Certificates[0].Code);
    }

    [Fact]
    public async Task Wizard_GET_Defaults_To_IELTS_Preset_When_No_Params()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var result = await controller.Wizard(null, null, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/CertificateWizard.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model).Form;
        Assert.Equal("IELTS", vm.Code);
        Assert.Equal("ielts_academic", vm.PresetCode);
        Assert.Equal("avg_half_step", vm.ScoreAggregationRule);
        Assert.Single(vm.Skills);
        Assert.Equal("LIST", vm.Skills[0].Code);
    }

    [Fact]
    public async Task Wizard_GET_Populates_Requested_Preset()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var result = await controller.Wizard("toeic_sw", null, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model).Form;
        Assert.Equal("TOEIC_SW", vm.Code);
        Assert.Equal("toeic_sw", vm.PresetCode);
        Assert.Equal("independent", vm.ScoreAggregationRule);
        Assert.Single(vm.Skills);
        Assert.Equal("SPEA", vm.Skills[0].Code);
    }

    [Fact]
    public async Task Wizard_GET_With_CertId_Loads_Blueprint_For_Edit()
    {
        var api = new TestAdminCertificatesClient
        {
            BlueprintToReturn = new CertificateBlueprintDetailDto(
                CertId: 42,
                Code: "EDIT42",
                Name: "Edit Test 42",
                Description: "Editing cert 42",
                ScoreAggregationRule: "avg_whole",
                PresetCode: "custom",
                IsActive: true,
                Skills:
                [
                    new SkillBlueprintDetailDto(101, 42, "SK1", "Skill 1", ModalityDto.Reading, 1, 60,
                    [
                        new SectionBlueprintDetailDto(201, 101, 42, "SEC1", "Section 1", 1, 1.0m, NavigationModeDto.FullSandbox, null, LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 20, null, null)
                    ])
                ],
                Bands: [])
        };
        var controller = CreateController(api);

        var result = await controller.Wizard(null, 42, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model).Form;
        Assert.Equal(42, vm.CertId);
        Assert.Equal("EDIT42", vm.Code);
        Assert.Equal("Edit Test 42", vm.Name);
        Assert.Single(vm.Skills);
        Assert.Equal(101, vm.Skills[0].SkillId);
        Assert.Equal(201, vm.Skills[0].Sections[0].SectionId);
    }

    [Fact]
    public async Task SaveWizard_POST_Saves_And_Redirects_To_Index_With_TempData()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var model = new CertificateWizardViewModel
        {
            Code = "NEW_CERT",
            Name = "New Certificate",
            ScoreAggregationRule = "sum",
            PresetCode = "custom",
            IsActive = true,
            Skills =
            [
                new WizardSkillViewModel
                {
                    Code = "S1",
                    Name = "Skill 1",
                    Modality = ModalityDto.Writing,
                    DisplayOrder = 1,
                    DurationMinutes = 60,
                    Sections =
                    [
                        new WizardSectionViewModel
                        {
                            Code = "W1",
                            Name = "Writing 1",
                            DisplayOrder = 1,
                            ScoreWeight = 1.0m,
                            NavigationMode = NavigationModeDto.HybridGroupLocked,
                            LayoutType = LayoutTypeDto.SplitHorizontal,
                            TimerMode = "SECTION_SHARED",
                            OptionCount = 4,
                            ClusterSize = 1,
                            UnitCount = 1,
                            RequiredKeywordCount = 2,
                            ExpectedQuestionCount = 5,
                            ToolWordCount = true,
                            ToolDisableSpellcheck = true,
                            ComponentFreeResponse = true
                        }
                    ]
                }
            ]
        };

        var result = await controller.SaveWizard(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminCertificatesController.Index), redirect.ActionName);
        Assert.NotNull(api.LastSavedRequest);
        Assert.Equal("NEW_CERT", api.LastSavedRequest.Code);
        var savedSec = api.LastSavedRequest.Skills[0].Sections[0];
        Assert.Equal(NavigationModeDto.HybridGroupLocked, savedSec.NavigationMode);
        Assert.Equal(2, savedSec.RequiredKeywordCount);
        Assert.Contains("wordCount", savedSec.ToolsConfigJson!);
        Assert.Contains("free_response", savedSec.AllowedComponentTypesJson!);
        Assert.Contains("New Certificate", (string)controller.TempData["Success"]!);
    }

    [Fact]
    public async Task SaveWizard_POST_Handles_ApiException_And_Returns_View_With_Error()
    {
        var api = new TestAdminCertificatesClient { ThrowOnSave = true };
        var controller = CreateController(api);

        var model = new CertificateWizardViewModel
        {
            Code = "FAIL_CERT",
            Name = "Failed Certificate"
        };

        var result = await controller.SaveWizard(model, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/CertificateWizard.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<CertificateWizardPageViewModel>(viewResult.Model).Form;
        Assert.Equal("Skill code is missing", vm.ErrorMessage);
    }

    [Fact]
    public async Task PresetsJson_Returns_Json_List_Of_Presets()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var result = await controller.PresetsJson(CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var presets = Assert.IsAssignableFrom<IReadOnlyList<CertificateBlueprintPresetDto>>(jsonResult.Value);
        Assert.Equal(2, presets.Count);
    }

    private sealed class TestAdminScoreConversionClient : IAdminScoreConversionClient
    {
        public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminScoreConversionDto(1, "IELTS", "IELTS", [], null, null, [], []));

        public Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminScoreConversionDto(certId, "IELTS", "IELTS", [], null, null, [], []));

        public Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScorePreviewResultDto(
                request.ScoreAggregationRule,
                request.Skills.Select(s => new SkillScorePreviewResultDto(s.SkillId, s.SkillCode, s.RawScore ?? 0m, 65, null)).ToList(),
                65,
                "BAND_6.5",
                "Competent User Plus"));
    }

    [Fact]
    public async Task Wizard_GET_Clamps_Step_To_4()
    {
        var api = new TestAdminCertificatesClient();
        var controller = CreateController(api);

        var resultStep4 = await controller.Wizard(null, null, 4, CancellationToken.None);
        var viewResult4 = Assert.IsType<ViewResult>(resultStep4);
        var vm4 = Assert.IsType<CertificateWizardPageViewModel>(viewResult4.Model).Form;
        Assert.Equal(4, vm4.Step);

        var resultStep5 = await controller.Wizard(null, null, 5, CancellationToken.None);
        var viewResult5 = Assert.IsType<ViewResult>(resultStep5);
        var vm5 = Assert.IsType<CertificateWizardPageViewModel>(viewResult5.Model).Form;
        Assert.Equal(4, vm5.Step);
    }

    [Fact]
    public async Task ScorePreview_POST_Returns_Json_Score_Preview_Result()
    {
        var api = new TestAdminCertificatesClient();
        var scoreClient = new TestAdminScoreConversionClient();
        var controller = CreateController(api, scoreClient);

        var request = new ScorePreviewRequestDto(
            CertId: 1,
            CertCode: "IELTS",
            ScoreAggregationRule: "avg_half_step",
            Skills: [new SkillScoreInputDto(1, "LIST", 65m, null)]);

        var result = await controller.ScorePreview(request, CancellationToken.None);
        var jsonResult = Assert.IsType<JsonResult>(result);
        var preview = Assert.IsType<ScorePreviewResultDto>(jsonResult.Value);
        Assert.Equal("avg_half_step", preview.ScoreAggregationRule);
        Assert.Equal(65, preview.TotalScore);
        Assert.Equal("BAND_6.5", preview.BandCode);
    }
}
