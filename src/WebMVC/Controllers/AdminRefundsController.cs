using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #281: the refund screens. <see cref="Permissions.SupportTriage"/> is the gate because a refund is
/// support work; the admin-only namespace governs the system, not a learner's money.
/// </summary>
[Authorize(Policy = Permissions.SupportTriage)]
[Route("admin/refunds")]
public sealed class AdminRefundsController(
    IAdminRefundsClient api,
    CurrentUser currentUser,
    ILogger<AdminRefundsController> logger) : AdminControllerBase(currentUser, logger)
{
    private DateTimeFormatter Dates => HttpContext.RequestServices?.GetService(typeof(DateTimeFormatter)) as DateTimeFormatter
        ?? new(new HttpContextAccessor { HttpContext = HttpContext });

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminRefundsAsync(q, status, page, 20, cancellationToken);
            return View("~/Views/Admin/Refunds.cshtml",
                new AdminRefundsViewModel(summary, q, status, page).WithDates(Dates));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load refunds list");
            TempData["Error"] = "Could not load the refunds list. Please try again.";
            var empty = new AdminRefundsSummaryDto([], 0, 1, 20, 0, 0, 0, 0);
            return View("~/Views/Admin/Refunds.cshtml",
                new AdminRefundsViewModel(empty, q, status, page).WithDates(Dates));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await api.GetAdminRefundDetailAsync(id, cancellationToken);
            return View("~/Views/Admin/RefundDetail.cshtml",
                new AdminRefundDetailViewModel(detail).WithDates(Dates));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.RefundNotFound || ex.Status == 404)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load refund detail {RefundId}", id);
            TempData["Error"] = "Could not load the refund details.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] int userId,
        [FromForm] int creditCount,
        [FromForm] string? reason,
        [FromForm] int? orderId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            TempData["Error"] = "A learner is required.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required on every refund.";
            return RedirectToAction(nameof(Index));
        }

        if (creditCount <= 0)
        {
            TempData["Error"] = "A credit refund must return at least one credit.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            // The method is always Credit here: a money refund is #282, and the API refuses it.
            var request = new RequestRefundRequest(userId, RefundMethodDto.Credit, creditCount, reason.Trim(),
                orderId is > 0 ? orderId : null);
            var created = await api.RequestAdminRefundAsync(request, cancellationToken);
            TempData["Success"] = $"Refund #R-{created.RefundId} recorded and waiting for a decision.";
            return RedirectToAction(nameof(Detail), new { id = created.RefundId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to record refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record refund for learner {UserId}", userId);
            TempData["Error"] = "Could not record the refund. Please try again.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.ApproveAdminRefundAsync(id, cancellationToken);
            TempData["Success"] = $"Refund #R-{id} approved and the credits are in the learner's balance.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to approve refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve refund {RefundId}", id);
            TempData["Error"] = "Could not approve the refund. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("{id:int}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int id,
        [FromForm] string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required to reject a refund.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        try
        {
            await api.RejectAdminRefundAsync(id, new RejectRefundRequest(reason.Trim()), cancellationToken);
            TempData["Success"] = $"Refund #R-{id} rejected. Nothing was paid.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to reject refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reject refund {RefundId}", id);
            TempData["Error"] = "Could not reject the refund. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }
}
