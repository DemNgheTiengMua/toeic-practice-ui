using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/users")]
public sealed class AdminUsersController(
    IAdminUsersClient api,
    CurrentUser currentUser,
    ILogger<AdminUsersController> logger) : AdminControllerBase(currentUser, logger)
{

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int? adjustUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminUsersAsync(q, status, page, 20, cancellationToken);

            AdminUserDto? adjustUser = null;
            if (adjustUserId.HasValue)
            {
                adjustUser = summary.Users.FirstOrDefault(u => u.UserId == adjustUserId.Value);
            }

            var vm = new AdminUsersViewModel(summary, q, status, page, adjustUser);
            return View("~/Views/Admin/Users.cshtml", vm);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load admin students list");
            TempData["Error"] = "Could not load the students list. Please try again.";
            var emptySummary = new AdminUsersSummaryDto([], 0, 1, 20, 0, 0, 0);
            return View("~/Views/Admin/Users.cshtml", new AdminUsersViewModel(emptySummary, q, status, page));
        }
    }

    [HttpPost("{id:int}/lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(int id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await api.LockUserAsync(id, cancellationToken);
            TempData["Success"] = $"Account '{updated.Email}' has been locked. The lock takes effect immediately.";
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to lock user {UserId}", id);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await api.UnlockUserAsync(id, cancellationToken);
            TempData["Success"] = $"Account '{updated.Email}' has been unlocked.";
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to unlock user {UserId}", id);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/resend-activation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendActivation(int id, CancellationToken cancellationToken)
    {
        try
        {
            await api.ResendUserActivationAsync(id, cancellationToken);
            TempData["Success"] = "Activation email has been resent.";
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to resend activation for user {UserId}", id);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/adjust-credits")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustCredits(
        int id,
        [FromForm] int delta,
        [FromForm] string reason,
        CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            TempData["Error"] = "Credits to add must not be zero.";
            return RedirectToAction(nameof(Index), new { adjustUserId = id });
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Reason is required for credit adjustments.";
            return RedirectToAction(nameof(Index), new { adjustUserId = id });
        }

        try
        {
            var result = await api.AdjustUserCreditsAsync(id, new AdjustCreditsRequest(delta, reason), cancellationToken);
            TempData["Success"] = $"Adjusted credits by {(delta > 0 ? $"+{delta}" : delta.ToString())}. New balance is {result.NewBalance} credits.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            TempData["Error"] = "Deduction cannot exceed the student's current balance.";
            return RedirectToAction(nameof(Index), new { adjustUserId = id });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to adjust credits for user {UserId}", id);
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { adjustUserId = id });
        }
    }
}
