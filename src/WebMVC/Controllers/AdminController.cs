using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Admin-only pages. The policy refuses a learner before any action runs; the API
/// enforces the same rule on its own endpoints, so a forged or replayed cookie gets nothing.</summary>
[Authorize(Policy = Roles.Admin)]
public sealed class AdminController(
    IAdminDashboardClient api,
    CurrentUser currentUser,
    ILogger<AdminController>? logger = null) : AdminControllerBase(currentUser, logger)
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await api.GetAdminDashboardAsync(cancellationToken);
            return View(new WebMVC.Models.AdminDashboardViewModel(dashboard ?? AdminDashboardDto.Empty));
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Could not load admin dashboard metrics");
            TempData["Error"] = "Could not load the dashboard metrics. Please try again.";
            return View(new WebMVC.Models.AdminDashboardViewModel(AdminDashboardDto.Empty));
        }
    }
}
