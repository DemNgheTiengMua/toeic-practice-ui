using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>
/// Question batch import (ADR-0007).
/// Legacy /admin/imports route smoothly redirects to integrated exam package import on /admin/exams.
/// </summary>
[Authorize(Policy = Roles.Admin)]
[Route("admin/imports")]
public sealed class AdminImportsController(
    IAdminImportsClient api,
    CurrentUser currentUser,
    ILogger<AdminImportsController> logger) : AdminControllerBase(currentUser, logger)
{
    /// <summary>Matches the API's package limit (QuestionImportService.MaxUploadBytes) plus form overhead.</summary>
    private const long MaxUploadBytes = 101L * 1024 * 1024;

    [HttpGet("")]
    public IActionResult Index([FromQuery] int? certId = null)
    {
        return RedirectToAction("Index", "AdminExams", new { certId, import = true });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload([FromForm] int? certId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (certId is null)
        {
            TempData["Error"] = "Choose the certificate the questions belong to.";
            return RedirectToAction("Index", "AdminExams");
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a .json manifest or a .zip package to import.";
            return RedirectToAction("Index", "AdminExams", new { certId, import = true });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await api.UploadAdminImportAsync(certId.Value, stream, file.FileName, cancellationToken);

            if (result.Status == ImportBatchStatusDto.Done)
            {
                TempData["Success"] = $"Imported '{result.ExamName}' as a draft exam with {result.QuestionCount} questions.";
            }
            else
            {
                TempData["Error"] = $"Nothing was imported: {result.ErrorCount} {(result.ErrorCount == 1 ? "problem" : "problems")} found in manifest.";
            }

            return RedirectToAction("Index", "AdminExams", new { certId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Import upload refused");
            TempData["Error"] = ex.Detail ?? ex.Title ?? "The import was refused.";
            return RedirectToAction("Index", "AdminExams", new { certId });
        }
    }
}
