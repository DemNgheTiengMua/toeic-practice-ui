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
            // #243 / D19: the level and modality registries are the layers a page may not call, so
            // the words are rendered here. The href is built here too, from the lesson id for THIS
            // cell - a view that assembled it would be free to point at the wrong level.
            var rows = data.Topics.Select(c => Row(c)).ToList();

            return View(new WeaknessAnalysisViewModel(data, TempData["ErrorMessage"] as string, TempData["SuccessMessage"] as string)
            {
                InsightPanel = new("dashboard", insight?.CertId ?? 0, insight),
                TopicRows = rows,
                // The service marked exactly one row; finding it among the prepared rows rather than
                // rebuilding it keeps one row shape for both places the page shows it.
                WeakestTopic = rows.FirstOrDefault(r => r.IsWeakest)
            });

            TopicAccuracyRowViewModel Row(TopicAccuracyDto c) => new(
                c.TopicId,
                c.TopicCode,
                c.TopicName,
                CefrLevelLabels.LabelForCode(c.LevelCode, logger),
                ModalityLabels.LabelForCode(c.Modality.ToWireCode(), logger),
                c.Answered,
                c.Correct,
                c.AccuracyPercent,
                c.IsWeakest,
                c.Answered >= c.ObservationFloor,
                c.Answered >= c.ObservationFloor
                    ? $"{c.Correct}/{c.Answered} correct"
                    : $"{c.Answered} of the {c.ObservationFloor} answers this needs before it can be called a weakness",
                c.LessonId is { } lessonId ? $"/lessons/{lessonId}" : null);
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
