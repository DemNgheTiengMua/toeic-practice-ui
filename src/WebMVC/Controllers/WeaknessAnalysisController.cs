using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class WeaknessAnalysisController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<WeaknessAnalysisController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    private bool IsAjaxRequest =>
        Request.Headers.XRequestedWith == "XMLHttpRequest" ||
        Request.Headers.Accept.ToString().Contains("application/json");

    [HttpGet("/weakness-analysis")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var data = await Api.GetWeaknessAnalysisAsync(bearer, cancellationToken);
            return View(new WeaknessAnalysisViewModel(data, TempData["ErrorMessage"] as string, TempData["SuccessMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load weakness analysis");
            TempData["ShellError"] = "Could not load weakness analysis. Please check your connection and try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpPost("/weakness-analysis/recommendations/{recId:int}/resolve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveRecommendation(int recId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var res = await Api.ResolveRecommendationAsync(recId, bearer, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(new { success = true, recId = res.RecId, isResolved = res.IsResolved });
            }

            TempData["SuccessMessage"] = "Recommendation marked as resolved.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to resolve recommendation {RecId}", recId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }

            TempData["ErrorMessage"] = ex.Detail ?? "Could not resolve recommendation. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }
}
