using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>Question batch import (ADR-0007): upload a manifest or package, see each batch's log.</summary>
[Authorize(Policy = Roles.Admin)]
[Route("admin/imports")]
public sealed class AdminImportsController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<AdminImportsController> logger) : Controller
{
    /// <summary>Matches the API's package limit (QuestionImportService.MaxUploadBytes) plus form overhead.</summary>
    private const long MaxUploadBytes = 101L * 1024 * 1024;

    private async Task<string> BearerAsync() => (await currentUser.GetBearerTokenAsync())!;

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? certId, [FromQuery] int? batchId, CancellationToken cancellationToken)
    {
        var bearer = await BearerAsync();
        AdminImportBatchesDto batches;
        try
        {
            batches = await api.GetAdminImportBatchesAsync(certId, bearer, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load import batches");
            TempData["Error"] = "Could not load the import history. Please try again.";
            return View("~/Views/Admin/Imports.cshtml", new AdminImportsViewModel(
                new AdminImportBatchesDto([], []), certId, null));
        }

        AdminImportBatchDetailDto? selected = null;
        if (batchId is { } id)
        {
            try
            {
                selected = await api.GetAdminImportBatchAsync(id, bearer, cancellationToken);
            }
            catch (ApiException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                TempData["Error"] = $"Import batch {id} was not found.";
            }
        }

        return View("~/Views/Admin/Imports.cshtml", new AdminImportsViewModel(
            batches, certId, selected));
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
            return RedirectToAction(nameof(Index));
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a .json manifest or a .zip package to import.";
            return RedirectToAction(nameof(Index), new { certId });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await api.UploadAdminImportAsync(certId.Value, stream, file.FileName, await BearerAsync(), cancellationToken);

            if (result.Status == ImportBatchStatusDto.Done)
            {
                TempData["Success"] = $"Imported '{result.ExamName}' as a draft exam with {result.QuestionCount} questions.";
            }
            else
            {
                TempData["Error"] = $"Nothing was imported: {result.ErrorCount} {(result.ErrorCount == 1 ? "problem" : "problems")} found. The log below lists each one.";
            }

            return RedirectToAction(nameof(Index), new { certId, batchId = result.BatchId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Import upload refused");
            TempData["Error"] = ex.Detail ?? ex.Title ?? "The import was refused.";
            return RedirectToAction(nameof(Index), new { certId });
        }
    }
}
