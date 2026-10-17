using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Route("admin/lessons")]
public sealed class AdminLessonsController(
    IAdminLessonsClient api,
    IAdminImportsClient importsClient,
    CurrentUser currentUser,
    ILogger<AdminLessonsController> logger) : AdminControllerBase(currentUser, logger)
{
    private const long MaxUploadBytes = 101L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? certId, [FromQuery] int? skillId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdminCertificateOptionDto> certificates = Array.Empty<AdminCertificateOptionDto>();
        try
        {
            var batches = await importsClient.GetAdminImportBatchesAsync(null, cancellationToken);
            certificates = batches.Certificates;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load certificate options for admin lessons");
        }

        IReadOnlyList<AdminLessonSummaryDto> lessons = Array.Empty<AdminLessonSummaryDto>();
        try
        {
            lessons = await api.GetAdminLessonsAsync(certId, skillId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load admin lessons list");
            TempData["Error"] = "Could not load the lessons list. Please try again.";
        }

        var vm = new AdminLessonListViewModel(
            lessons,
            certificates,
            certId,
            skillId,
            TempData["Error"] as string,
            TempData["Success"] as string);

        return View("~/Views/Admin/Lessons/Index.cshtml", vm);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create([FromQuery] int? certId, CancellationToken cancellationToken = default)
    {
        var certificates = await LoadCertificatesAsync(cancellationToken);
        var selectedCertId = certId ?? certificates.FirstOrDefault()?.CertId ?? 1;

        var vm = new AdminLessonEditViewModel
        {
            LessonId = 0,
            CertId = selectedCertId,
            EstimatedMinutes = 5,
            DisplayOrder = 0,
            Certificates = certificates,
            Cards = new List<AdminLessonCardEditViewModel>
            {
                new()
                {
                    CardType = "rule",
                    Title = "Core Rule",
                    ContentMarkdown = "### Rule Title\n\nExplain the foundational rule here.",
                    DisplayOrder = 0
                }
            }
        };

        return View("~/Views/Admin/Lessons/Edit.cshtml", vm);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var certificates = await LoadCertificatesAsync(cancellationToken);
        AdminLessonDetailDto lesson;
        try
        {
            lesson = await api.GetAdminLessonByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load lesson {LessonId} for edit", id);
            TempData["Error"] = $"Lesson #{id} was not found.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new AdminLessonEditViewModel
        {
            LessonId = lesson.LessonId,
            CertId = lesson.CertId,
            CertCode = lesson.CertCode,
            SkillId = lesson.SkillId,
            SkillCode = lesson.SkillCode,
            CategoryId = lesson.CategoryId,
            CategoryCode = lesson.CategoryCode,
            Title = lesson.Title,
            Summary = lesson.Summary,
            EstimatedMinutes = lesson.EstimatedMinutes,
            DisplayOrder = lesson.DisplayOrder,
            Certificates = certificates,
            Cards = lesson.Cards.Select((c, idx) => new AdminLessonCardEditViewModel
            {
                CardId = c.CardId,
                CardType = c.CardType,
                Title = c.Title,
                ContentMarkdown = c.ContentMarkdown,
                QuestionPrompt = c.QuestionPrompt,
                OptionA = c.OptionA,
                OptionB = c.OptionB,
                OptionC = c.OptionC,
                OptionD = c.OptionD,
                CorrectOption = c.CorrectOption,
                Explanation = c.Explanation,
                Rationale = c.Rationale,
                DisplayOrder = idx
            }).ToList()
        };

        return View("~/Views/Admin/Lessons/Edit.cshtml", vm);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] AdminLessonEditViewModel vm, CancellationToken cancellationToken = default)
    {
        if (vm.Cards is null || vm.Cards.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "At least one card is required.");
        }

        if (!ModelState.IsValid)
        {
            vm.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/Lessons/Edit.cshtml", vm);
        }

        var cardsDto = (vm.Cards ?? new List<AdminLessonCardEditViewModel>()).Select((c, idx) => new AdminLessonCardUpsertDto(
            c.CardId,
            c.CardType,
            c.Title,
            c.ContentMarkdown,
            c.QuestionPrompt,
            c.OptionA,
            c.OptionB,
            c.OptionC,
            c.OptionD,
            c.CorrectOption,
            c.Explanation,
            c.Rationale,
            idx
        )).ToList();

        var request = new AdminLessonUpsertDto(
            vm.CertId,
            vm.SkillId,
            vm.CategoryId,
            vm.Title,
            vm.Summary,
            vm.EstimatedMinutes,
            vm.DisplayOrder,
            cardsDto
        );

        try
        {
            if (vm.LessonId == 0)
            {
                await api.CreateAdminLessonAsync(request, cancellationToken);
                TempData["Success"] = $"Lesson '{vm.Title}' created successfully.";
            }
            else
            {
                await api.UpdateAdminLessonAsync(vm.LessonId, request, cancellationToken);
                TempData["Success"] = $"Lesson '{vm.Title}' updated successfully.";
            }

            return RedirectToAction(nameof(Index), new { certId = vm.CertId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "API rejected saving lesson: {Code}", ex.Code);
            vm.ErrorMessage = ex.Detail ?? ex.Title ?? ex.Message;
            vm.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/Lessons/Edit.cshtml", vm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save lesson");
            vm.ErrorMessage = ex.Message;
            vm.Certificates = await LoadCertificatesAsync(cancellationToken);
            return View("~/Views/Admin/Lessons/Edit.cshtml", vm);
        }
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.DeleteAdminLessonAsync(id, cancellationToken);
            TempData["Success"] = $"Lesson #{id} deleted successfully.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "API refused deletion of lesson {LessonId}: {Code}", id, ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete lesson {LessonId}", id);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload([FromForm] int certId, [FromForm] bool overwrite, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a .json manifest or a .zip package to import.";
            return RedirectToAction(nameof(Index), new { certId });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await api.UploadAdminLessonImportAsync(certId, overwrite, stream, file.FileName, cancellationToken);
            TempData["Success"] = $"Import completed: {result.CreatedCount} created, {result.UpdatedCount} updated ({result.TotalCardsImported} cards total).";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Lesson import failed: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during lesson import");
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { certId });
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await api.ExportAdminLessonAsync(id, cancellationToken);
            var json = JsonSerializer.Serialize(manifest, JsonExportOptions);
            return File(Encoding.UTF8.GetBytes(json), "application/json", $"lesson-{id}.json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export lesson {LessonId}", id);
            TempData["Error"] = $"Could not export lesson #{id}: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("template")]
    public async Task<IActionResult> Template(CancellationToken cancellationToken = default)
    {
        try
        {
            var template = await api.GetAdminLessonTemplateAsync(cancellationToken);
            var json = JsonSerializer.Serialize(template, JsonExportOptions);
            return File(Encoding.UTF8.GetBytes(json), "application/json", "lesson-template.json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download lesson template");
            TempData["Error"] = "Could not download sample template.";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IReadOnlyList<AdminCertificateOptionDto>> LoadCertificatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batches = await importsClient.GetAdminImportBatchesAsync(null, cancellationToken);
            return batches.Certificates;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load certificate options");
            return Array.Empty<AdminCertificateOptionDto>();
        }
    }
}
