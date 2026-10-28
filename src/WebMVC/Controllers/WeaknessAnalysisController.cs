using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class WeaknessAnalysisController(
    IWeaknessAnalysisClient api,
    CurrentUser currentUser,
    ILogger<WeaknessAnalysisController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    private bool IsAjaxRequest =>
        Request.Headers.XRequestedWith == "XMLHttpRequest" ||
        Request.Headers.Accept.ToString().Contains("application/json");

    [HttpGet("/weakness-analysis")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var data = await api.GetWeaknessAnalysisAsync(cancellationToken);

            AiInsightDto? insight = null;
            try
            {
                insight = await api.GetDashboardInsightAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not pre-fetch AI insight for weakness analysis");
            }
            return View(new WeaknessAnalysisViewModel(data, TempData["ErrorMessage"] as string, TempData["SuccessMessage"] as string)
            {
                InsightPanel = new("dashboard", insight?.CertId ?? 0, insight)
            });
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
            var res = await api.ResolveRecommendationAsync(recId, cancellationToken);

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
