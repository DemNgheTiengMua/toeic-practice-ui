using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class WalletController(
    IWalletClient api,
    CurrentUser currentUser,
    ILogger<WalletController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/wallet")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await api.GetWalletAsync(cancellationToken);
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
