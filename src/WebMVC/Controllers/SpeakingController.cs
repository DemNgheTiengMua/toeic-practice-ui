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
    ISpeakingClient api,
    ISkillPracticeClient catalog,
    CurrentUser currentUser,
    ILogger<SpeakingController> logger) : LearnerControllerBase(currentUser, logger)
{
    /// <summary>#159: the door. A spoken answer cannot be typed, so speaking is not part of the drill;
    /// its sidebar entry lands on the list of recorded practice exams instead. This is what the
    /// "Available in full mock exams" grey card should always have been.</summary>
    [HttpGet("/speaking")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var list = await catalog.GetSpeakingExamsAsync(cancellationToken);
            return View(new SkillPracticeIndexViewModel("Speaking", "/speaking", list, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load the speaking practice list");
            TempData["ShellError"] = "Could not load speaking practice. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet("/speaking/{id:int}/instructions")]
    public async Task<IActionResult> Instructions(int id, CancellationToken cancellationToken)
    {
        try
        {
            var instructions = await api.GetSpeakingInstructionsAsync(id, cancellationToken);
            return View(new SpeakingInstructionsViewModel(instructions, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            logger.LogWarning(ex, "Speaking exam {ExamId} not found", id);
            TempData["ShellError"] = "Speaking practice exam not found.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load instructions for speaking exam {ExamId}", id);
            TempData["ShellError"] = "Could not load instructions.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/speaking/{id:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await api.StartSpeakingAttemptAsync(id, cancellationToken);
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
            var take = await api.GetSpeakingTakeAsync(attemptId, q, cancellationToken);

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
            return RedirectToAction(nameof(Index));
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
            return RedirectToAction(nameof(Index));
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
            using var stream = audioFile.OpenReadStream();
            var result = await api.UploadSpeakingAudioAsync(
                attemptId,
                questionId,
                stream,
                audioFile.FileName,
                audioFile.ContentType,
                durationMs,
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
            await api.SubmitSpeakingAttemptAsync(attemptId, cancellationToken);
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
            var pending = await api.GetSpeakingGradingPendingAsync(attemptId, cancellationToken);

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
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/speaking/status/{attemptId:int}")]
    public async Task<IActionResult> Status(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await api.GetSpeakingGradingPendingAsync(attemptId, cancellationToken);
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
            await api.RetrySpeakingGradingAsync(attemptId, cancellationToken);
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
            var review = await api.GetSpeakingReviewAsync(attemptId, cancellationToken);
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
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/speaking/audio/{attemptId:int}/{questionId:int}")]
    public async Task<IActionResult> Audio(int attemptId, int questionId, CancellationToken cancellationToken)
    {
        try
        {
            var (stream, contentType, fileName) = await api.GetSpeakingAudioStreamAsync(attemptId, questionId, cancellationToken);
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
