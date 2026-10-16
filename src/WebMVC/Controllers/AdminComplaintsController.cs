using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/complaints")]
public sealed class AdminComplaintsController(
    IAdminComplaintsClient api,
    CurrentUser currentUser,
    ILogger<AdminComplaintsController> logger) : Controller
{

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminComplaintsAsync(q, status, page, 20, cancellationToken);
            var vm = new AdminComplaintsViewModel(summary, q, status, page);
            return View("~/Views/Admin/Complaints.cshtml", vm);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load complaints list");
            TempData["Error"] = "Could not load the complaints list. Please try again.";
            var emptySummary = new AdminComplaintsSummaryDto([], 0, 1, 20, 0, 0, null, null, null);
            return View("~/Views/Admin/Complaints.cshtml", new AdminComplaintsViewModel(emptySummary, q, status, page));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await api.GetAdminComplaintDetailAsync(id, cancellationToken);
            if (detail is null)
            {
                return NotFound();
            }

            var vm = new AdminComplaintDetailViewModel(detail);
            return View("~/Views/Admin/ComplaintDetail.cshtml", vm);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.ComplaintNotFound || ex.Status == 404)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load complaint detail {ComplaintId}", id);
            TempData["Error"] = "Could not load the complaint details.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("{id:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(
        int id,
        [FromForm] string status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(status) || !EnumMapping.TryParseWireCode<ComplaintStatusDto>(status, out var statusDto))
        {
            TempData["Error"] = "Invalid complaint status.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        try
        {
            await api.SetAdminComplaintStatusAsync(id, new UpdateComplaintStatusRequest(statusDto), cancellationToken);
            TempData["Success"] = $"Complaint status updated to '{status}'.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to update complaint status: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update status for complaint {ComplaintId}", id);
            TempData["Error"] = "Could not update status. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("{id:int}/resolve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int id,
        [FromForm] string? resolution,
        [FromForm] string? internalNotes,
        [FromForm] string? issue_credit,
        [FromForm] int? credit_amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            TempData["Error"] = "Resolution reply for the learner is required.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (string.IsNullOrWhiteSpace(internalNotes))
        {
            TempData["Error"] = "Internal notes for the admin record are required.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var shouldIssue = !string.IsNullOrEmpty(issue_credit) && (issue_credit.Equals("on", StringComparison.OrdinalIgnoreCase) || issue_credit.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (shouldIssue && credit_amount.HasValue && credit_amount.Value > 20)
        {
            TempData["Error"] = "Grant credits must be between 1 and 20.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var grantCredits = shouldIssue && credit_amount.HasValue && credit_amount.Value > 0 ? credit_amount.Value : 0;

        try
        {
            var request = new ResolveComplaintRequest(resolution.Trim(), internalNotes.Trim(), grantCredits);
            await api.ResolveAdminComplaintAsync(id, request, cancellationToken);
            TempData["Success"] = "Complaint resolved. The learner has been notified.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to resolve complaint: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve complaint {ComplaintId}", id);
            TempData["Error"] = "Could not resolve complaint. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }
}
