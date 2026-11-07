using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #206 / S-03: the stratified audit and quarantine queue.
/// <para>
/// <b>Blind solve is enforced by the UI's order, and the order is the API's.</b> This controller never
/// holds a key: <see cref="IAdminAuditClient.GetAuditQueueAsync"/> returns none, and the only call that
/// returns one is <see cref="Reveal"/>, whose server-side precondition is a stored solve. The screen's
/// sequence - solve, then reveal, then judge - is therefore a rendering of what the API already
/// refuses, not a rule this controller is trusted to remember. A reviewer who reads the API directly
/// meets the same wall.
/// </para>
/// <para>
/// ADR-0002: the browser posts here, never to the API, and every forward carries the anti-forgery
/// token.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.ContentReview)]
[Route("admin/audit")]
public sealed class AdminAuditController(
    IAdminAuditClient api,
    CurrentUser currentUser,
    ILogger<AdminAuditController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpGet("{batchId:int}")]
    public async Task<IActionResult> Queue(int batchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queue = await api.GetAuditQueueAsync(batchId, cancellationToken);
            return View("~/Views/Admin/AuditQueue.cshtml", AuditPresentation.FromQueue(
                queue,
                canDecide: true,
                message: TempData["Success"] as string,
                error: TempData["Error"] as string));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load the audit queue for batch {BatchId}", batchId);
            TempData["Error"] = ex.Detail ?? "Could not load the audit queue.";
            return RedirectToAction("Batch", "AdminIngestion", new { batchId });
        }
    }

    /// <summary>Draws the stratified sample, if one has not been drawn yet. Idempotent on the service.</summary>
    [HttpPost("{batchId:int}/draw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Draw(int batchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var queue = await api.DrawSampleAsync(batchId, cancellationToken);
            TempData["Success"] = queue.Strata.Count == 1
                ? "Drew the sample across 1 cell, one item per cell at minimum."
                : $"Drew the sample across {queue.Strata.Count} cells, one item per cell at minimum.";
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not draw a sample for batch {BatchId}", batchId);
            TempData["Error"] = ex.Detail ?? "The sample could not be drawn.";
        }

        return RedirectToAction(nameof(Queue), new { batchId });
    }

    /// <summary>
    /// W-01's first half: the reviewer's own answer. Posted before the key exists on the page at all,
    /// which is why this route returns to the queue rather than to a reveal.
    /// </summary>
    [HttpPost("{batchId:int}/samples/{sampleItemId:int}/solve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Solve(
        int batchId,
        int sampleItemId,
        [FromForm] string? label,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await api.RecordSolveAsync(sampleItemId, label ?? string.Empty, cancellationToken);
            TempData["Success"] = $"Solve recorded for sample {sampleItemId}. The authored key can now be read.";
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not record a solve for sample {SampleItemId}", sampleItemId);
            TempData["Error"] = ex.Detail ?? "The solve was refused.";
        }

        return RedirectToAction(nameof(Queue), new { batchId });
    }

    /// <summary>
    /// W-01's second half, and the only route on this screen that returns a key. The API refuses it
    /// until a solve is stored; a reviewer who navigates here directly is refused the same way.
    /// </summary>
    [HttpPost("{batchId:int}/strata/{stratumId:int}/samples/{sampleItemId:int}/reveal")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reveal(
        int batchId,
        int stratumId,
        int sampleItemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reveal = await api.RevealSampleKeyAsync(sampleItemId, cancellationToken);
            return View("~/Views/Admin/AuditReveal.cshtml", AuditPresentation.FromReveal(
                batchId, stratumId, reveal, message: null, error: null));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not reveal the key of sample {SampleItemId}", sampleItemId);
            TempData["Error"] = ex.Detail ?? "The key cannot be read yet.";
            return RedirectToAction(nameof(Queue), new { batchId });
        }
    }

    /// <summary>Records the verdict, which is the comparison the reveal made possible.</summary>
    [HttpPost("{batchId:int}/samples/{sampleItemId:int}/verdict")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verdict(
        int batchId,
        int sampleItemId,
        [FromForm] string? verdict,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await api.RecordVerdictAsync(sampleItemId, verdict ?? string.Empty, cancellationToken);
            TempData["Success"] = verdict switch
            {
                "key_error" => "Recorded a key error. One of these quarantines the whole stratum.",
                "non_key_defect" => "Recorded a non-key defect.",
                _ => "Recorded a correct key.",
            };
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not record a verdict for sample {SampleItemId}", sampleItemId);
            TempData["Error"] = ex.Detail ?? "The verdict was refused.";
        }

        return RedirectToAction(nameof(Queue), new { batchId });
    }

    /// <summary>
    /// Takes the stratum's decision. <c>approve</c> is ignored: the decision is derived from the verdicts,
    /// so a screen that offered a choice could approve a stratum containing a confirmed key error.
    /// </summary>
    [HttpPost("{batchId:int}/strata/{stratumId:int}/decide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int batchId, int stratumId, CancellationToken cancellationToken = default)
    {
        try
        {
            var decision = await api.DecideStratumAsync(stratumId, cancellationToken);
            TempData["Success"] = decision.Status switch
            {
                "approved" => $"Stratum {stratumId} approved: {decision.SampledPromoted} reviewed and "
                    + $"{decision.UnsampledPromoted} unreviewed items promoted together.",
                _ => $"Stratum {stratumId} quarantined ({decision.Quarantined} items). "
                    + "A systematic defect is not promoted on the strength of a sample.",
            };
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not decide stratum {StratumId}", stratumId);
            TempData["Error"] = ex.Detail ?? "The decision was refused.";
        }

        return RedirectToAction(nameof(Queue), new { batchId });
    }
}
