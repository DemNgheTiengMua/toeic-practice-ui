using System.Text.Json;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class AdminCertificatesController(
    CurrentUser currentUser,
    IAdminCertificatesClient api,
    ILogger<AdminCertificatesController> logger,
    IAdminScoreConversionClient? scoreConversionClient = null)
    : AdminControllerBase(currentUser, logger)
{
    [HttpGet("/admin/certificates")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var certificates = await api.GetAdminCertificatesAsync(await BearerAsync(), cancellationToken);
            return View("~/Views/Admin/Certificates.cshtml", new AdminCertificateListViewModel { Certificates = certificates });
        }
        // HttpRequestException, not ApiException alone: with the API down HttpClient throws a plain
        // transport fault. A dead session (AUTH_LOCKED / AUTH_REQUIRED) still reaches the sign-out
        // middleware in Program.cs, as in LearnerControllerBase. Same for the catches below.
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            logger.LogError(ex, "Could not load the admin certificate list");
            return View("~/Views/Admin/Certificates.cshtml", new AdminCertificateListViewModel
            {
                ErrorMessage = "Could not reach the certificate service. The list below may be empty — reload in a moment.",
            });
        }
    }

    [HttpGet("/admin/certificates/wizard")]
    public async Task<IActionResult> Wizard(string? preset, int? certId, int step = 1, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CertificateBlueprintPresetDto> presets;
        try
        {
            presets = await api.GetPresetsAsync(await BearerAsync(), cancellationToken);
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // Without the presets there is nothing to seed the wizard from, but the admin should
            // see why rather than a blank step list.
            logger.LogError(ex, "Could not load certificate presets for the wizard");
            return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(
                new CertificateWizardViewModel
                {
                    Step = Math.Clamp(step, 1, 4),
                    ErrorMessage = "The certificate service did not answer, so the blueprint presets could not be loaded. Reload the wizard to try again.",
                }));
        }

        var vm = new CertificateWizardViewModel
        {
            Presets = presets,
            Step = Math.Clamp(step, 1, 4)
        };

        if (certId.HasValue && certId.Value > 0)
        {
            CertificateBlueprintDetailDto? detail = null;
            try
            {
                detail = await api.GetCertificateBlueprintAsync(certId.Value, await BearerAsync(), cancellationToken);
            }
            catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
            {
                // Without the saved blueprint the form would fall back to the default preset with
                // no CertId, and saving it would create a new certificate. Offer a retry instead.
                // An unknown certId (a stale or hand-edited link) is the caller's mistake, not a
                // fault: log it as a warning so it does not read as a server error in the logs.
                logger.Log(ex is ApiException { Status: >= 400 and < 500 } ? LogLevel.Warning : LogLevel.Error,
                    ex, "Could not load certificate blueprint {CertId} for editing", certId.Value);
                return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(
                    new CertificateWizardViewModel
                    {
                        Step = Math.Clamp(step, 1, 4),
                        ErrorMessage = "Could not load this certificate's blueprint, so it cannot be edited right now. Nothing was changed.",
                    })
                {
                    BlueprintRetryUrl = $"/admin/certificates/wizard?certId={certId.Value}",
                });
            }

            if (detail is not null)
            {
                vm.CertId = detail.CertId;
                vm.Code = detail.Code;
                vm.Name = detail.Name;
                vm.Description = detail.Description;
                vm.ScoreAggregationRule = detail.ScoreAggregationRule;
                vm.PresetCode = detail.PresetCode;
                vm.IsActive = detail.IsActive;
                vm.Skills = detail.Skills.Select(s => new WizardSkillViewModel
                {
                    SkillId = s.SkillId,
                    Code = s.Code,
                    Name = s.Name,
                    Modality = s.Modality,
                    DisplayOrder = s.DisplayOrder,
                    DurationMinutes = s.DurationMinutes,
                    Sections = s.Sections.Select(MapDetailSectionToViewModel).ToList()
                }).ToList();
                return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(vm));
            }
        }

        var chosenPreset = presets.FirstOrDefault(p => string.Equals(p.PresetCode, preset, StringComparison.OrdinalIgnoreCase))
                           ?? presets.FirstOrDefault(p => p.PresetCode == "ielts_academic")
                           ?? presets.FirstOrDefault();

        if (chosenPreset is not null)
        {
            PopulateFromPreset(vm, chosenPreset);
        }

        return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(vm));
    }

    [HttpPost("/admin/certificates/wizard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWizard(CertificateWizardViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            model.Presets = await api.GetPresetsAsync(await BearerAsync(), cancellationToken);
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // Without presets the step 1 grid would render empty on redisplay, so fail the whole
            // save the same way the GET does rather than half-populating the form.
            logger.LogError(ex, "Could not load certificate presets while saving the wizard");
            model.ErrorMessage = "The certificate service did not answer, so nothing was saved. Reload the wizard and try again.";
            return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(model));
        }

        try
        {
            var request = MapViewModelToRequest(model);
            var result = await api.SaveCertificateWizardAsync(request, await BearerAsync(), cancellationToken);
            TempData["Success"] = $"Certificate '{result.Name}' ({result.Code}) saved with {result.SkillsCount} skills and {result.SectionsCount} sections.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            model.ErrorMessage = ex.Detail ?? ex.Message;
            return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(model));
        }
        catch (Exception ex)
        {
            model.ErrorMessage = ex.Message;
            return View("~/Views/Admin/CertificateWizard.cshtml", new CertificateWizardPageViewModel(model));
        }
    }

    [HttpPost("/admin/certificates/score-preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScorePreview([FromBody] ScorePreviewRequestDto request, CancellationToken cancellationToken)
    {
        if (scoreConversionClient is null)
        {
            return BadRequest(new { error = "Score conversion client is not configured." });
        }

        try
        {
            var result = await scoreConversionClient.CalculateScorePreviewAsync(request, await BearerAsync(), cancellationToken);
            return Json(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("/admin/certificates/presets-json")]
    public async Task<IActionResult> PresetsJson(CancellationToken cancellationToken)
    {
        var presets = await api.GetPresetsAsync(await BearerAsync(), cancellationToken);
        return Json(presets);
    }

    private static void PopulateFromPreset(CertificateWizardViewModel vm, CertificateBlueprintPresetDto preset)
    {
        vm.PresetCode = preset.PresetCode;
        vm.Code = preset.PresetCode switch
        {
            "ielts_academic" => "IELTS",
            "toeic_lr_std" => "TOEIC",
            "toeic_lr_cat" => "TOEIC_CAT",
            "toeic_sw" => "TOEIC_SW",
            "vstep" => "VSTEP",
            _ => "CUSTOM"
        };
        vm.Name = preset.Name;
        vm.Description = preset.Description;
        vm.ScoreAggregationRule = preset.ScoreAggregationRule;
        vm.IsActive = true;
        vm.Skills = preset.Skills.Select(s => new WizardSkillViewModel
        {
            Code = s.Code,
            Name = s.Name,
            Modality = s.Modality,
            DisplayOrder = s.DisplayOrder,
            DurationMinutes = s.DurationMinutes,
            Sections = s.Sections.Select(MapPresetSectionToViewModel).ToList()
        }).ToList();
    }

    private static WizardSectionViewModel MapPresetSectionToViewModel(SectionBlueprintPresetDto sec)
    {
        var vm = new WizardSectionViewModel
        {
            Code = sec.Code,
            Name = sec.Name,
            DisplayOrder = sec.DisplayOrder,
            ScoreWeight = sec.ScoreWeight,
            NavigationMode = sec.NavigationMode,
            TimerPolicyJson = sec.TimerPolicyJson,
            LayoutType = sec.LayoutType,
            OptionCount = sec.OptionCount,
            ClusterSize = sec.ClusterSize,
            UnitCount = sec.UnitCount,
            HideOptionText = sec.HideOptionText,
            RequirePassage = sec.RequirePassage,
            RequireAudio = sec.RequireAudio,
            RequireImage = sec.RequireImage,
            RequiredKeywordCount = sec.RequiredKeywordCount,
            ExpectedQuestionCount = sec.ExpectedQuestionCount,
            AllowedComponentTypesJson = sec.AllowedComponentTypesJson,
            ToolsConfigJson = sec.ToolsConfigJson
        };

        ExtractTimerPolicy(vm);
        ExtractToolsConfig(vm);
        ExtractAllowedComponents(vm);
        return vm;
    }

    private static WizardSectionViewModel MapDetailSectionToViewModel(SectionBlueprintDetailDto sec)
    {
        var vm = new WizardSectionViewModel
        {
            SectionId = sec.SectionId,
            Code = sec.Code,
            Name = sec.Name,
            DisplayOrder = sec.DisplayOrder,
            ScoreWeight = sec.ScoreWeight,
            NavigationMode = sec.NavigationMode,
            TimerPolicyJson = sec.TimerPolicyJson,
            LayoutType = sec.LayoutType,
            OptionCount = sec.OptionCount,
            ClusterSize = sec.ClusterSize,
            UnitCount = sec.UnitCount,
            HideOptionText = sec.HideOptionText,
            RequirePassage = sec.RequirePassage,
            RequireAudio = sec.RequireAudio,
            RequireImage = sec.RequireImage,
            RequiredKeywordCount = sec.RequiredKeywordCount,
            ExpectedQuestionCount = sec.ExpectedQuestionCount,
            AllowedComponentTypesJson = sec.AllowedComponentTypesJson,
            ToolsConfigJson = sec.ToolsConfigJson
        };

        ExtractTimerPolicy(vm);
        ExtractToolsConfig(vm);
        ExtractAllowedComponents(vm);
        return vm;
    }

    private static void ExtractTimerPolicy(WizardSectionViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.TimerPolicyJson))
        {
            vm.TimerMode = vm.NavigationMode switch
            {
                NavigationModeDto.AudioDriven => "AUDIO_PLUS_CHECK",
                NavigationModeDto.SystemPacedAuto => "ITEM_FIXED",
                _ => "SECTION_SHARED"
            };
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(vm.TimerPolicyJson);
            if (doc.RootElement.TryGetProperty("timerMode", out var tm))
            {
                vm.TimerMode = tm.GetString() ?? "SECTION_SHARED";
            }
            if (doc.RootElement.TryGetProperty("reviewSeconds", out var rs) && rs.TryGetInt32(out var val))
            {
                vm.ReviewSeconds = val;
            }
        }
        catch
        {
            vm.TimerMode = "SECTION_SHARED";
        }
    }

    private static void ExtractToolsConfig(WizardSectionViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.ToolsConfigJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(vm.ToolsConfigJson);
            if (doc.RootElement.TryGetProperty("highlighter", out var hl) && hl.GetBoolean()) vm.ToolHighlighter = true;
            if (doc.RootElement.TryGetProperty("notes", out var nt) && nt.GetBoolean()) vm.ToolNotes = true;
            if (doc.RootElement.TryGetProperty("wordCount", out var wc) && wc.GetBoolean()) vm.ToolWordCount = true;
            if (doc.RootElement.TryGetProperty("disableSpellcheck", out var ds) && ds.GetBoolean()) vm.ToolDisableSpellcheck = true;
            if (doc.RootElement.TryGetProperty("cutPaste", out var cp) && cp.GetBoolean()) vm.ToolCutPaste = true;
        }
        catch
        {
            // Ignore malformed json
        }
    }

    private static void ExtractAllowedComponents(WizardSectionViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.AllowedComponentTypesJson))
        {
            vm.ComponentMcq = true;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(vm.AllowedComponentTypesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                vm.ComponentMcq = false;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var type = el.GetString();
                    if (type == "mcq") vm.ComponentMcq = true;
                    else if (type == "mcq_3") vm.ComponentMcq3 = true;
                    else if (type == "multi_select") vm.ComponentMultiSelect = true;
                    else if (type == "inline_completion") vm.ComponentInlineCompletion = true;
                    else if (type == "dropdown") vm.ComponentDropdown = true;
                    else if (type == "drag_drop") vm.ComponentDragDrop = true;
                    else if (type == "free_response") vm.ComponentFreeResponse = true;
                }
            }
        }
        catch
        {
            vm.ComponentMcq = true;
        }
    }

    private static SaveCertificateWizardRequest MapViewModelToRequest(CertificateWizardViewModel vm)
    {
        var skills = vm.Skills.Select((s, sIdx) => new SaveSkillWizardDto(
            SkillId: s.SkillId,
            Code: s.Code,
            Name: s.Name,
            Modality: s.Modality,
            DisplayOrder: s.DisplayOrder > 0 ? s.DisplayOrder : sIdx + 1,
            DurationMinutes: s.DurationMinutes,
            Sections: s.Sections.Select((sec, secIdx) =>
            {
                // Build TimerPolicyJson
                string? timerPolicyJson = sec.TimerMode switch
                {
                    "AUDIO_PLUS_CHECK" => JsonSerializer.Serialize(new { timerMode = "AUDIO_PLUS_CHECK", reviewSeconds = sec.ReviewSeconds ?? 120 }),
                    "ITEM_FIXED" => JsonSerializer.Serialize(new { timerMode = "ITEM_FIXED" }),
                    _ => JsonSerializer.Serialize(new { timerMode = "SECTION_SHARED" })
                };

                // Build ToolsConfigJson
                var tools = new Dictionary<string, bool>();
                if (sec.ToolHighlighter)
                {
                    tools["highlighter"] = true;
                    tools["notes"] = true;
                }
                if (sec.ToolNotes) tools["notes"] = true;
                if (sec.ToolWordCount) tools["wordCount"] = true;
                if (sec.ToolDisableSpellcheck) tools["disableSpellcheck"] = true;
                if (sec.ToolCutPaste) tools["cutPaste"] = true;
                string? toolsConfigJson = tools.Count > 0 ? JsonSerializer.Serialize(tools) : null;

                // Build AllowedComponentTypesJson
                var components = new List<string>();
                if (sec.ComponentMcq) components.Add("mcq");
                if (sec.ComponentMcq3) components.Add("mcq_3");
                if (sec.ComponentMultiSelect) components.Add("multi_select");
                if (sec.ComponentInlineCompletion) components.Add("inline_completion");
                if (sec.ComponentDropdown) components.Add("dropdown");
                if (sec.ComponentDragDrop) components.Add("drag_drop");
                if (sec.ComponentFreeResponse) components.Add("free_response");
                string? allowedComponentsJson = components.Count > 0 ? JsonSerializer.Serialize(components) : null;

                return new SaveSectionWizardDto(
                    SectionId: sec.SectionId,
                    Code: sec.Code,
                    Name: sec.Name,
                    DisplayOrder: sec.DisplayOrder > 0 ? sec.DisplayOrder : secIdx + 1,
                    ScoreWeight: sec.ScoreWeight > 0 ? sec.ScoreWeight : 1.0m,
                    NavigationMode: sec.NavigationMode,
                    TimerPolicyJson: timerPolicyJson,
                    LayoutType: sec.LayoutType,
                    OptionCount: sec.OptionCount > 0 ? sec.OptionCount : 4,
                    ClusterSize: sec.ClusterSize > 0 ? sec.ClusterSize : 1,
                    UnitCount: sec.UnitCount > 0 ? sec.UnitCount : 1,
                    HideOptionText: sec.HideOptionText,
                    RequirePassage: sec.RequirePassage,
                    RequireAudio: sec.RequireAudio,
                    RequireImage: sec.RequireImage,
                    RequiredKeywordCount: sec.RequiredKeywordCount,
                    ExpectedQuestionCount: sec.ExpectedQuestionCount,
                    AllowedComponentTypesJson: allowedComponentsJson,
                    ToolsConfigJson: toolsConfigJson);
            }).ToList()
        )).ToList();

        return new SaveCertificateWizardRequest(
            CertId: vm.CertId,
            Code: vm.Code,
            Name: vm.Name,
            Description: vm.Description,
            ScoreAggregationRule: vm.ScoreAggregationRule,
            PresetCode: vm.PresetCode,
            IsActive: vm.IsActive,
            Skills: skills);
    }
}
