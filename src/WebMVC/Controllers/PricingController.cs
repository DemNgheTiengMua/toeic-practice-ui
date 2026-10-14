using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

[AllowAnonymous]
[Route("pricing")]
public sealed class PricingController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<PricingController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (currentUser.IsSignedIn && currentUser.Role == Roles.Learner)
        {
            try
            {
                var token = await currentUser.GetBearerTokenAsync();
                if (!string.IsNullOrEmpty(token))
                {
                    var shell = await api.GetLearnerContextAsync(token, cancellationToken);
                    LearnerShell.Set(HttpContext, shell);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load learner shell context for pricing page");
            }
        }

        try
        {
            var packages = await api.GetPublicPackagesAsync(cancellationToken);
            return View(packages);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load public packages for pricing page");
            return View("PricingUnavailable");
        }
    }
}
