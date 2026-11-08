using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Learner)]
public sealed class LearningPathController(
    ILearningPathClient api,
    CurrentUser currentUser,
    ILogger<LearningPathController> logger,
    IPlacementClient? placementClient = null)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/learning-path")]
    [HttpGet("/learning-path/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await api.GetLearningPathAsync(cancellationToken);
            AiInsightDto? insight = null;
            if (summary is { HasActivePath: true, LearnerPathId: { } pathId })
            {
                try
                {
                    insight = await api.GetPathInsightAsync(cancellationToken);
                }
                catch (ApiException ex)
                {
                    logger.LogWarning(ex, "Failed to load path insight for path {PathId}", pathId);
                }
            }

            var insightPanel = summary is { HasActivePath: true, LearnerPathId: { } pId }
                ? new AiInsightPanelViewModel("path", pId, insight)
                : null;

            // #156: the calibration check rides along with the path screen. A failure here must not
            // hide the path itself — the detector is an addition, not a precondition.
            CalibrationViewDto? calibration = null;
            try
            {
                calibration = placementClient is null
                    ? null
                    : await placementClient.GetCalibrationAsync(cancellationToken);
            }
            catch (ApiException ex)
            {
                logger.LogWarning(ex, "Failed to load calibration state");
            }

            return View(new LearningPathIndexViewModel(
                summary!, TempData["ErrorMessage"] as string, insightPanel, TempData["SuccessMessage"] as string,
                calibration));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load learning path summary");
            TempData["ShellError"] = "Could not load your learning path. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpPost("/learning-path/generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate([FromForm] string? targetBandCode, CancellationToken cancellationToken)
    {
        try
        {
            await api.GenerateLearningPathAsync(new GenerateLearningPathRequest(targetBandCode), cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.LearningPathAlreadyActive)
        {
            logger.LogWarning(ex, "Learning path is already active");
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.LearningPathGoalUnrealistic or ApiError.Codes.LearningPathBandUnknown)
        {
            // #153: a refusal the learner can act on. The service's message carries the two numbers,
            // so it is shown verbatim instead of the generic "try again".
            logger.LogInformation(ex, "Path generation refused: {Code}", ex.Code);
            TempData["ErrorMessage"] = ex.Detail ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to generate learning path");
            TempData["ErrorMessage"] = ex.Detail ?? "Could not generate learning path. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/learning-path/steps/{stepId:int}")]
    public async Task<IActionResult> Step(int stepId, CancellationToken cancellationToken)
    {
        try
        {
            var step = await api.GetLearningPathStepAsync(stepId, cancellationToken);
            if (step is null)
            {
                TempData["ShellError"] = "Step was not found.";
                return RedirectToAction(nameof(Index));
            }

            return View(step);
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load learning path step {StepId}", stepId);
            TempData["ShellError"] = "Could not load the step.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/learning-path/steps/{stepId:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartStep(int stepId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.StartLearningPathStepAsync(stepId, cancellationToken);

            // #235 / PLC-60: a step whose cell was cooling is served from a sibling topic, and the
            // learner is told which — a silent substitution reads as "the plan ignored my topic".
            if (result.SubstitutedFromTopicId is not null)
            {
                TempData["SuccessMessage"] =
                    "Your step's own topic is resting today, so these items come from a related topic in the same band. "
                    + "Your path has not changed.";
            }

            return Redirect(result.RedirectUrl);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.LearningPathStepLocked)
        {
            logger.LogWarning(ex, "Step {StepId} is locked", stepId);
            TempData["ErrorMessage"] = ex.Detail ?? ex.Message ?? "This step is locked. Complete previous steps first.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start step {StepId}", stepId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start the step. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// #156: the learner's answer to a calibration proposal. It runs through the API so the same
    /// rule (recompute at click time, refuse a stale proposal) applies no matter who calls it.
    /// </summary>
    [HttpPost("/learning-path/calibration")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calibration([FromForm] bool adjust, CancellationToken cancellationToken)
    {
        if (placementClient is null)
        {
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var result = await placementClient.DecideCalibrationAsync(new CalibrationDecisionRequest(adjust), cancellationToken);
            TempData["SuccessMessage"] = result.PathAdjusted
                ? $"Your starting band is now {result.ProposedBandCode}. Your path keeps your completed steps."
                : "Your path is unchanged. You will be asked again after more practice.";
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.Validation)
        {
            TempData["ErrorMessage"] = ex.Detail ?? "That choice could not be recorded.";
        }
        catch (ApiException ex)
        {
            // A stale proposal is the ordinary case here, not a fault: the evidence moved on since
            // the page was rendered. Say that, and reload the page it came from.
            logger.LogInformation(ex, "Calibration decision refused: {Code}", ex.Code);
            TempData["ErrorMessage"] = "That proposal is out of date. The current evidence is shown below.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/learning-path/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        try
        {
            await api.ResetLearningPathAsync(cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to reset learning path");
            TempData["ErrorMessage"] = ex.Detail ?? "Could not reset learning path.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/learner/insights/path")]
    public async Task<IActionResult> GetPathInsight(CancellationToken cancellationToken)
    {
        try
        {
            var insight = await api.GetPathInsightAsync(cancellationToken);
            return Ok(insight);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/learner/insights/path/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryPathInsight(CancellationToken cancellationToken)
    {
        try
        {
            await api.RetryPathInsightAsync(cancellationToken);
            return Ok(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }
}
