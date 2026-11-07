using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #205 / S-02: the bulk ingestion and staging workbench.
/// <para>
/// <b>Two doors, deliberately.</b> Uploading is authoring (<c>pool.content.author</c>); clearing a
/// staged item for service is review (<c>pool.content.review</c>). One screen, because the author who
/// dropped the file must see what it refused - but a different capability for the act that makes
/// content live, so an account that may import may not also publish what it imported (ADM-02's first
/// arm, and the reason D16 splits the namespaces at all).
/// </para>
/// <para>
/// Nothing here judges a record. Every verdict, line, rule and state comes from
/// <see cref="IAdminIngestionClient"/>; this controller groups and forwards, as
/// <see cref="AdminCoverageController"/> does. A page that recomputed a verdict would eventually
/// disagree with the gate that refused the save.
/// </para>
/// <para>
/// ADR-0002: the browser posts here, never to the API. The JWT stays in the HttpOnly cookie and every
/// forward carries the anti-forgery token.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/ingestion")]
public sealed class AdminIngestionController(
    IAdminIngestionClient api,
    CurrentUser currentUser,
    ILogger<AdminIngestionController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await api.GetWorkbenchBatchesAsync(cancellationToken);
            return View("~/Views/Admin/Ingestion.cshtml",
                IngestionPresentation.FromBatches(list, message: null, error: null));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load the ingestion batch list");
            return View("~/Views/Admin/Ingestion.cshtml",
                new IngestionBatchListViewModel([], "Could not load the batch list. Please try again.", null));
        }
    }

    /// <summary>
    /// The verdict list. This is the screen the ticket is about: one row per record, the line it opened
    /// on, the rule that refused it, and the record itself so it can be corrected where it stands.
    /// </summary>
    [HttpGet("{batchId:int}")]
    public async Task<IActionResult> Batch(int batchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await api.GetWorkbenchBatchAsync(batchId, cancellationToken);
            return View("~/Views/Admin/IngestionBatch.cshtml",
                IngestionPresentation.FromDetail(
                    detail,
                    CurrentUser.HasPermission(Permissions.ContentReview),
                    message: TempData["Success"] as string,
                    error: TempData["Error"] as string));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load ingestion batch {BatchId}", batchId);
            TempData["Error"] = ex.Detail ?? "Could not load the batch.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// The upload. A file that the reader cannot parse still leaves a batch naming who sent it, so a
    /// failure here redirects to a batch rather than losing the attempt.
    /// </summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] string? skill,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            TempData["Error"] = "Choose the skill this file is for.";
            return RedirectToAction(nameof(Index));
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a skill content pool file to ingest.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await api.UploadWorkbenchFileAsync(skill, stream, file.FileName, cancellationToken);

            TempData["Success"] =
                $"Batch {result.BatchId}: {result.Accepted} accepted, {result.Rejected} refused.";
            return RedirectToAction(nameof(Batch), new { batchId = result.BatchId });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Ingestion upload refused");
            TempData["Error"] = ex.Detail ?? "The upload was refused.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// AC3: one corrected record, re-judged. The textarea's own record id names which record is being
    /// corrected, so a page cannot submit a correction against a line it did not show.
    /// </summary>
    [HttpPost("{batchId:int}/records/{line:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resubmit(
        int batchId,
        int line,
        [FromForm] string? record,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record))
        {
            TempData["Error"] = "The corrected record is empty.";
            return RedirectToAction(nameof(Batch), new { batchId });
        }

        try
        {
            var verdict = await api.ResubmitWorkbenchRecordAsync(batchId, line, record, cancellationToken);
            TempData[verdict.Accepted ? "Success" : "Error"] = verdict.Accepted
                ? $"Line {line} was corrected and is now staged for review."
                : $"Line {line} was judged again and refused: {verdict.Message}";
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not re-judge line {Line} of batch {BatchId}", line, batchId);
            TempData["Error"] = ex.Detail ?? "The correction was refused.";
        }

        return RedirectToAction(nameof(Batch), new { batchId });
    }

    /// <summary>
    /// AC2's second half: the audit's clearance. Gated on review rather than authoring, because
    /// publishing is not the act of importing.
    /// </summary>
    [HttpPost("{batchId:int}/items/{itemId:int}/clear")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ContentReview)]
    public async Task<IActionResult> Clear(
        int batchId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await api.ClearWorkbenchItemAsync(batchId, itemId, cancellationToken);
            TempData["Success"] = $"Item {itemId} cleared for service.";
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not clear item {ItemId} of batch {BatchId}", itemId, batchId);
            TempData["Error"] = ex.Detail ?? "The clearance was refused.";
        }

        return RedirectToAction(nameof(Batch), new { batchId });
    }

    /// <summary>The same ceiling the exam importer uses; a skill pool file is smaller in practice.</summary>
    private const long MaxUploadBytes = 100L * 1024 * 1024;
}
