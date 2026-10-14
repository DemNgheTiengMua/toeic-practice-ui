using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class WalletController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<WalletController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/wallet")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var wallet = await Api.GetWalletAsync(bearer, cancellationToken);
            return View(new WalletViewModel(wallet));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load wallet");
            TempData["ShellError"] = "Could not load your credit wallet. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
