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
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<LearningPathController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/learning-path")]
    [HttpGet("/learning-path/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var summary = await Api.GetLearningPathAsync(bearer, cancellationToken);
            return View(new LearningPathIndexViewModel(summary, TempData["ErrorMessage"] as string));
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
            var bearer = await BearerAsync();
            await Api.GenerateLearningPathAsync(new GenerateLearningPathRequest(targetBandCode), bearer, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.LearningPathAlreadyActive)
        {
            logger.LogWarning(ex, "Learning path is already active");
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to generate learning path");
            TempData["ErrorMessage"] = ex.Detail ?? "Could not generate learning path. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/learning-path/modules/{moduleId:int}")]
    public async Task<IActionResult> ModuleDetail(int moduleId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var detail = await Api.GetLearningPathModuleAsync(moduleId, bearer, cancellationToken);
            return View(new LearningPathModuleDetailViewModel(detail, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.LearningPathModuleNotFound || ex.Code == ApiError.Codes.LearningPathNotFound)
        {
            logger.LogWarning(ex, "Learning path module {ModuleId} not found", moduleId);
            TempData["ShellError"] = "Module was not found.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load learning path module {ModuleId}", moduleId);
            TempData["ShellError"] = "Could not load the module detail.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/learning-path/steps/{stepId:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartStep(int stepId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var result = await Api.StartLearningPathStepAsync(stepId, bearer, cancellationToken);
            return Redirect(result.RedirectUrl);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.LearningPathStepLocked)
        {
            logger.LogWarning(ex, "Step {StepId} is locked", stepId);
            TempData["ErrorMessage"] = "This step is locked. Complete previous steps first.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start step {StepId}", stepId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start the step. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/learning-path/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.ResetLearningPathAsync(bearer, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to reset learning path");
            TempData["ErrorMessage"] = ex.Detail ?? "Could not reset learning path.";
            return RedirectToAction(nameof(Index));
        }
    }
}
