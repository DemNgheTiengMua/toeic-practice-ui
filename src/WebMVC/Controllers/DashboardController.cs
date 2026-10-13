using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>The learner's landing page after sign-in.</summary>
[Authorize]
public sealed class DashboardController(CurrentUser currentUser, ICompanionApiClient api, ILogger<DashboardController> logger) : Controller
{
    /// <param name="activation">"sent" or "failed" after the reminder's Resend button.</param>
    public async Task<IActionResult> Index(string? activation, CancellationToken cancellationToken)
    {
        // Verification is read live, not from the cookie: the learner may have just clicked the link.
        bool? emailVerified = null;
        try
        {
            var me = await api.GetMeAsync((await currentUser.GetBearerTokenAsync())!, cancellationToken);
            emailVerified = me.EmailVerified;
        }
        catch (HttpRequestException ex) when (ex is not ApiException)
        {
            // API unreachable: render without the reminder rather than fail the page.
            logger.LogWarning(ex, "Could not read verification status for the dashboard");
        }

        return View(new DashboardViewModel(currentUser, emailVerified, activation));
    }
}
