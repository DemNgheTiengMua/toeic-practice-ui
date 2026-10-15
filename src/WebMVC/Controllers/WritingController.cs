using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class WritingController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<WritingController> logger) : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/writing/{id:int}/instructions")]
    public async Task<IActionResult> Instructions(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var instructions = await Api.GetWritingInstructionsAsync(id, bearer, cancellationToken);
            return View(new WritingInstructionsViewModel(instructions, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            logger.LogWarning(ex, "Writing exam {ExamId} not found", id);
            TempData["ShellError"] = "Writing practice exam not found.";
            return RedirectToAction("Index", "Practice");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load instructions for writing exam {ExamId}", id);
            TempData["ShellError"] = "Could not load instructions.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpPost("/writing/{id:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var response = await Api.StartWritingAttemptAsync(id, bearer, cancellationToken);
            return RedirectToAction(nameof(Take), new { attemptId = response.AttemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to start writing exam {ExamId}", id);
            TempData["ErrorMessage"] = "You do not have enough credits to start writing practice.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start writing practice {ExamId}", id);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start writing practice. Please try again.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
    }

    [HttpGet("/writing/take/{attemptId:int}")]
    public async Task<IActionResult> Take(int attemptId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var take = await Api.GetWritingTakeAsync(attemptId, q, bearer, cancellationToken);

            if (take.Status == AttemptStatusDto.Graded)
            {
                return RedirectToAction(nameof(Review), new { attemptId });
            }

            if (take.Status == AttemptStatusDto.Submitted)
            {
                return RedirectToAction(nameof(Pending), new { attemptId });
            }

            return View(new WritingTakeViewModel(take, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptExpired)
        {
            logger.LogInformation("Writing attempt {AttemptId} has expired", attemptId);
            TempData["ShellError"] = "This writing practice attempt has expired.";
            return RedirectToAction("Index", "Practice");
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Writing attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load writing attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load writing practice attempt.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpPost("/writing/save-draft")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft([FromBody] SaveWritingDraftAjaxModel model, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var request = new SaveWritingDraftRequest(model.QuestionId, model.ResponseText);
            var result = await Api.SaveWritingDraftAsync(model.AttemptId, request, bearer, cancellationToken);
            return Ok(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save draft for attempt {AttemptId}, question {QuestionId}", model.AttemptId, model.QuestionId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/writing/submit/{attemptId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.SubmitWritingAttemptAsync(attemptId, bearer, cancellationToken);
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to submit writing attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = "You do not have enough credits to submit for AI grading.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to submit writing attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Failed to submit attempt. Please try again.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
    }

    [HttpGet("/writing/pending/{attemptId:int}")]
    public async Task<IActionResult> Pending(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var pending = await Api.GetWritingGradingPendingAsync(attemptId, bearer, cancellationToken);

            if (pending.AttemptStatus == AttemptStatusDto.Graded)
            {
                return RedirectToAction(nameof(Review), new { attemptId });
            }

            return View(new WritingPendingViewModel(pending, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Writing attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load pending grading for attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load grading status.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpGet("/writing/status/{attemptId:int}")]
    public async Task<IActionResult> Status(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var pending = await Api.GetWritingGradingPendingAsync(attemptId, bearer, cancellationToken);
            return Ok(pending);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/writing/retry/{attemptId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.RetryWritingGradingAsync(attemptId, bearer, cancellationToken);
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to retry grading for attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not retry grading. Please try again.";
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
    }

    [HttpGet("/writing/review/{attemptId:int}")]
    public async Task<IActionResult> Review(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var review = await Api.GetWritingReviewAsync(attemptId, bearer, cancellationToken);
            return View(new WritingReviewViewModel(review, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptNotFinished)
        {
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Writing attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load writing review for attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load writing review.";
            return RedirectToAction("Index", "Practice");
        }
    }
}
