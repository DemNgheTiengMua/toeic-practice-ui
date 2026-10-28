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
    private DateTimeFormatter Dates => HttpContext.RequestServices?.GetService(typeof(DateTimeFormatter)) as DateTimeFormatter
        ?? new(new HttpContextAccessor { HttpContext = HttpContext });


    [HttpGet("/wallet")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await api.GetWalletAsync(cancellationToken);
            return View((new WalletViewModel(wallet)).WithDates(Dates));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load wallet");
            TempData["ShellError"] = "Could not load your credit wallet. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
