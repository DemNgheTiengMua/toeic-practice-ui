using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class SpeakingController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<SpeakingController> logger) : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/speaking/{id:int}/instructions")]
    public async Task<IActionResult> Instructions(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var instructions = await Api.GetSpeakingInstructionsAsync(id, bearer, cancellationToken);
            return View(new SpeakingInstructionsViewModel(instructions, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            logger.LogWarning(ex, "Speaking exam {ExamId} not found", id);
            TempData["ShellError"] = "Speaking practice exam not found.";
            return RedirectToAction("Index", "Practice");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load instructions for speaking exam {ExamId}", id);
            TempData["ShellError"] = "Could not load instructions.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpPost("/speaking/{id:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var response = await Api.StartSpeakingAttemptAsync(id, bearer, cancellationToken);
            return RedirectToAction(nameof(Take), new { attemptId = response.AttemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to start speaking exam {ExamId}", id);
            TempData["ErrorMessage"] = "You do not have enough credits to start speaking practice.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start speaking practice {ExamId}", id);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start speaking practice. Please try again.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
    }

    [HttpGet("/speaking/take/{attemptId:int}")]
    public async Task<IActionResult> Take(int attemptId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var take = await Api.GetSpeakingTakeAsync(attemptId, q, bearer, cancellationToken);

            if (take.Status == AttemptStatusDto.Graded)
            {
                return RedirectToAction(nameof(Review), new { attemptId });
            }

            if (take.Status == AttemptStatusDto.Submitted)
            {
                return RedirectToAction(nameof(Pending), new { attemptId });
            }

            return View(new SpeakingTakeViewModel(take, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptExpired)
        {
            logger.LogInformation("Speaking attempt {AttemptId} has expired", attemptId);
            TempData["ShellError"] = "This speaking practice attempt has expired.";
            return RedirectToAction("Index", "Practice");
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Speaking attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load speaking attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load speaking practice attempt.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpPost("/speaking/upload-audio")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAudio(
        [FromForm] int attemptId,
        [FromForm] int questionId,
        [FromForm] IFormFile? audioFile,
        [FromForm] int? durationMs,
        CancellationToken cancellationToken)
    {
        if (audioFile is null || audioFile.Length == 0)
        {
            return BadRequest(new { code = ApiError.Codes.Validation, message = "Audio file cannot be empty." });
        }

        try
        {
            var bearer = await BearerAsync();
            using var stream = audioFile.OpenReadStream();
            var result = await Api.UploadSpeakingAudioAsync(
                attemptId,
                questionId,
                stream,
                audioFile.FileName,
                audioFile.ContentType,
                durationMs,
                bearer,
                cancellationToken);

            return Ok(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to upload audio for attempt {AttemptId}, question {QuestionId}", attemptId, questionId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpPost("/speaking/submit/{attemptId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.SubmitSpeakingAttemptAsync(attemptId, bearer, cancellationToken);
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to submit speaking attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = "You do not have enough credits to submit for AI grading.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to submit speaking attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Failed to submit attempt. Please try again.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
    }

    [HttpGet("/speaking/pending/{attemptId:int}")]
    public async Task<IActionResult> Pending(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var pending = await Api.GetSpeakingGradingPendingAsync(attemptId, bearer, cancellationToken);

            if (pending.AttemptStatus == AttemptStatusDto.Graded)
            {
                return RedirectToAction(nameof(Review), new { attemptId });
            }

            return View(new SpeakingPendingViewModel(pending, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Speaking attempt {AttemptId} not found", attemptId);
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

    [HttpGet("/speaking/status/{attemptId:int}")]
    public async Task<IActionResult> Status(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var pending = await Api.GetSpeakingGradingPendingAsync(attemptId, bearer, cancellationToken);
            return Ok(pending);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/speaking/retry/{attemptId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.RetrySpeakingGradingAsync(attemptId, bearer, cancellationToken);
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to retry grading for attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not retry grading. Please try again.";
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
    }

    [HttpGet("/speaking/review/{attemptId:int}")]
    public async Task<IActionResult> Review(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var review = await Api.GetSpeakingReviewAsync(attemptId, bearer, cancellationToken);
            return View(new SpeakingReviewViewModel(review, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptNotFinished)
        {
            return RedirectToAction(nameof(Pending), new { attemptId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Speaking attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load speaking review for attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load speaking review.";
            return RedirectToAction("Index", "Practice");
        }
    }

    [HttpGet("/speaking/audio/{attemptId:int}/{questionId:int}")]
    public async Task<IActionResult> Audio(int attemptId, int questionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var (stream, contentType, fileName) = await Api.GetSpeakingAudioStreamAsync(attemptId, questionId, bearer, cancellationToken);
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }
        catch (ApiException ex) when (ex.Status == 403 || ex.Code == ApiError.Codes.Forbidden)
        {
            return Forbid();
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            return NotFound();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to stream audio for attempt {AttemptId}, question {QuestionId}", attemptId, questionId);
            return StatusCode(ex.Status);
        }
    }
}
