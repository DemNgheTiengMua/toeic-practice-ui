using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Live;

namespace WebMVC.Controllers;

/// <summary>Admin-only pages. The policy refuses a learner before any action runs; the API
/// enforces the same rule on its own endpoints, so a forged or replayed cookie gets nothing.</summary>
[Authorize(Policy = Permissions.SystemAdmin)]
public sealed class AdminController(
    IAdminDashboardClient api,
    CurrentUser currentUser,
    ILogger<AdminController>? logger = null,
    WebMVC.Formatters.DateTimeFormatter? dates = null) : AdminControllerBase(currentUser, logger)
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await api.GetAdminDashboardAsync(cancellationToken);
            return View(Rendered(dashboard ?? AdminDashboardDto.Empty));
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Could not load admin dashboard metrics");
            TempData["Error"] = "Could not load the dashboard metrics. Please try again.";
            return View(Rendered(AdminDashboardDto.Empty));
        }
    }

    /// <summary>
    /// #286: the metric region on its own, for the live update to swap in. A partial rather than the page,
    /// so the response carries no layout, no <c>head</c> and no script tags - a full page assigned into the
    /// region would put a dashboard inside the dashboard.
    /// </summary>
    [HttpGet("/admin/dashboard/fragment")]
    public async Task<IActionResult> Fragment(CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await api.GetAdminDashboardAsync(cancellationToken);
            return PartialView("_DashboardMetrics", Rendered(dashboard ?? AdminDashboardDto.Empty));
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Could not load the dashboard fragment");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// #286 / ADR-0002: the stream the browser is allowed to hold open. It sits on this controller, so it
    /// carries the same <c>SystemAdmin</c> policy as the page whose figures it announces - a stream reachable
    /// without that authority would be a live feed of revenue to anyone holding a stale cookie. The bearer
    /// needed to read the revision has therefore been proved before this action runs.
    /// </summary>
    [HttpGet("/admin/dashboard/stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        var source = HttpContext.RequestServices.GetRequiredService<IDashboardRevisionSource>();
        await DashboardStream.WriteAsync(
            Response,
            DashboardStream.ReadingsAsync(source, DashboardStream.TickInterval, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// The figures plus the moment they describe, so a quiet dashboard can be told from a dead one. The
    /// stamp renders in the viewer's zone through the one formatter that does that (UI-52); the UTC fallback
    /// exists because the unit tests build this controller without a request culture.
    /// </summary>
    private WebMVC.Models.AdminDashboardViewModel Rendered(AdminDashboardDto dashboard)
    {
        var now = DateTimeOffset.UtcNow;
        var stamp = dates is null ? now.ToString("HH:mm:ss") + " UTC" : dates.Format(now);
        return new(dashboard, stamp);
    }
}
