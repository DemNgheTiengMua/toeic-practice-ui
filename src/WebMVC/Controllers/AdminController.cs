using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Admin-only pages. The policy refuses a learner before any action runs; the API
/// enforces the same rule on its own endpoints, so a forged or replayed cookie gets nothing.</summary>
[Authorize(Policy = "admin")]
public sealed class AdminController(ICompanionApiClient api, CurrentUser currentUser) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Proves the bearer forward works end to end: this API call carries the cookie's JWT.
        var me = await api.GetMeAsync(await currentUser.GetBearerTokenAsync() ?? "", cancellationToken);
        return View(me);
    }
}
