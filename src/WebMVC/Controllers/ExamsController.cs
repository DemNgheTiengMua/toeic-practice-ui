using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class ExamsController(
    IMockExamsClient api,
    CurrentUser currentUser,
    ILogger<ExamsController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/exams")]
    [HttpGet("/exams/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await api.GetMockExamsAsync(cancellationToken);
            return View(new ExamIndexViewModel(catalog, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load mock exam catalog");
            TempData["ShellError"] = "Could not load the mock exam list. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet("/exams/{id:int}/instructions")]
    public async Task<IActionResult> Instructions(int id, CancellationToken cancellationToken)
    {
        try
        {
            var instructions = await api.GetMockExamInstructionsAsync(id, cancellationToken);
            return View(new ExamInstructionsViewModel(instructions, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            logger.LogWarning(ex, "Mock exam not found: {ExamId}", id);
            TempData["ShellError"] = "The requested exam does not exist.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load mock exam instructions: {ExamId}", id);
            TempData["ShellError"] = "Could not load exam instructions.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/exams/{id:int}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await api.StartMockExamAsync(id, cancellationToken);
            var preflight = await api.GetPreflightStatusAsync(response.AttemptId, cancellationToken);
            if (preflight.RequiresPreflight && !preflight.IsVerified)
            {
                return RedirectToAction(nameof(Preflight), new { id, attemptId = response.AttemptId });
            }
            return RedirectToAction(nameof(Take), new { attemptId = response.AttemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to start mock exam {ExamId}", id);
            TempData["ErrorMessage"] = "You do not have enough credits to start this mock exam.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start mock exam {ExamId}", id);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start mock exam. Please try again.";
            return RedirectToAction(nameof(Instructions), new { id });
        }
    }

    [HttpGet("/exams/{id:int}/preflight")]
    [HttpGet("/exams/preflight/{attemptId:int}")]
    public async Task<IActionResult> Preflight(int? id, [FromQuery] int? attemptId, CancellationToken cancellationToken)
    {
        var effectiveAttemptId = attemptId ?? (RouteData.Values.TryGetValue("attemptId", out var raw) && int.TryParse(raw?.ToString(), out var parsed) ? parsed : (int?)null);

        if (!effectiveAttemptId.HasValue && id.HasValue)
        {
            try
            {
                var instructions = await api.GetMockExamInstructionsAsync(id.Value, cancellationToken);
                effectiveAttemptId = instructions.ActiveAttemptId;
            }
            catch (ApiException)
            {
            }
        }

        if (!effectiveAttemptId.HasValue)
        {
            TempData["ShellError"] = "Exam attempt not found.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var preflight = await api.GetPreflightStatusAsync(effectiveAttemptId.Value, cancellationToken);
            return View("Preflight", new ExamPreflightViewModel(preflight, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Attempt {AttemptId} not found for preflight", effectiveAttemptId.Value);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load preflight for attempt {AttemptId}", effectiveAttemptId.Value);
            TempData["ShellError"] = "Could not load preflight checklist.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/exams/preflight/{attemptId:int}/verify")]
    [HttpPost("/exams/{id:int}/preflight/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyPreflight(int? id, int? attemptId, [FromQuery] int? qAttemptId, CancellationToken cancellationToken)
    {
        var effectiveAttemptId = attemptId ?? qAttemptId ?? (id.HasValue && RouteData.Values.TryGetValue("attemptId", out var raw) && int.TryParse(raw?.ToString(), out var parsed) ? parsed : (int?)null);
        if (!effectiveAttemptId.HasValue)
        {
            return BadRequest();
        }

        try
        {
            await api.VerifyPreflightAsync(effectiveAttemptId.Value, cancellationToken);
            return RedirectToAction(nameof(Take), new { attemptId = effectiveAttemptId.Value });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to verify preflight for attempt {AttemptId}", effectiveAttemptId.Value);
            TempData["ErrorMessage"] = ex.Detail ?? "Failed to verify preflight checks. Please try again.";
            return RedirectToAction(nameof(Preflight), new { id, attemptId = effectiveAttemptId.Value });
        }
    }

    [HttpGet("/exams/{id:int}/take")]
    public async Task<IActionResult> TakeExam(int id, [FromQuery] int? attemptId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        if (attemptId.HasValue)
        {
            return await Take(attemptId.Value, q, cancellationToken);
        }

        try
        {
            var instructions = await api.GetMockExamInstructionsAsync(id, cancellationToken);
            if (instructions.ActiveAttemptId.HasValue)
            {
                return await Take(instructions.ActiveAttemptId.Value, q, cancellationToken);
            }
        }
        catch (ApiException)
        {
        }

        return RedirectToAction(nameof(Instructions), new { id });
    }

    [HttpGet("/exams/take/{attemptId:int}")]
    public async Task<IActionResult> Take(int attemptId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            var preflight = await api.GetPreflightStatusAsync(attemptId, cancellationToken);
            if (preflight.RequiresPreflight && !preflight.IsVerified)
            {
                return RedirectToAction(nameof(Preflight), new { id = preflight.ExamId, attemptId });
            }

            var take = await api.GetAttemptTakeAsync(attemptId, q, cancellationToken);

            if (take.Status is AttemptStatusDto.Submitted or AttemptStatusDto.Graded || take.IsAutoSubmitted)
            {
                return RedirectToAction(nameof(Result), new { attemptId });
            }

            return View(new ExamTakeViewModel(take, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PreflightRequired)
        {
            logger.LogInformation("Attempt {AttemptId} redirected to preflight verification", attemptId);
            return RedirectToAction(nameof(Preflight), new { attemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptExpired)
        {
            logger.LogInformation("Attempt {AttemptId} has expired and was auto-submitted", attemptId);
            return RedirectToAction(nameof(Result), new { attemptId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Attempt {AttemptId} not found", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load exam attempt.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/exams/take/{attemptId:int}/answers/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAnswer(
        int attemptId,
        int questionId,
        [FromForm] SaveAnswerFormModel form,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new SaveAnswerRequest(form.SelectedOptionId, form.IsFlagged, form.ResponseText);
            var result = await api.SaveAttemptAnswerAsync(attemptId, questionId, request, cancellationToken);

            return Json(new
            {
                questionId = result.QuestionId,
                selectedOptionId = result.SelectedOptionId,
                isFlagged = result.IsFlagged,
                remainingSeconds = result.RemainingSeconds,
                isAutoSubmitted = result.IsAutoSubmitted,
                redirectUrl = result.IsAutoSubmitted ? Url.Action(nameof(Result), new { attemptId }) : null
            });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptExpired)
        {
            logger.LogInformation("Attempt {AttemptId} expired during save answer for question {QuestionId}", attemptId, questionId);
            return Json(new
            {
                isAutoSubmitted = true,
                redirectUrl = Url.Action(nameof(Result), new { attemptId })
            });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save answer for attempt {AttemptId}, question {QuestionId}", attemptId, questionId);
            return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("/exams/take/{attemptId:int}/draft")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(
        int attemptId,
        [FromBody] SaveAttemptDraftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.SaveAttemptDraftAsync(attemptId, request, cancellationToken);
            return Ok(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save exam draft for attempt {AttemptId}, question {QuestionId}", attemptId, request.QuestionId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpPost("/exams/take/{attemptId:int}/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        int attemptId,
        CancellationToken cancellationToken,
        [FromForm] ExamSubmitFormModel? form = null)
    {
        try
        {
            if (form?.Drafts.Count > 0)
            {
                await api.SubmitAttemptWithDraftsAsync(
                    attemptId,
                    new SubmitAttemptRequest(form.Drafts),
                    cancellationToken);
            }
            else
            {
                await api.SubmitAttemptAsync(attemptId, cancellationToken);
            }
            return RedirectToAction(nameof(Result), new { attemptId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptExpired || ex.Code == ApiError.Codes.AttemptNotActive)
        {
            logger.LogInformation(ex, "Attempt {AttemptId} expired or already completed during submit", attemptId);
            return RedirectToAction(nameof(Result), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to submit attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Failed to submit exam attempt. Please try again.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
    }

    [HttpPost("/exams/take/{attemptId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            await api.CancelAttemptAsync(attemptId, cancellationToken);
            TempData["ShellSuccess"] = "The exam attempt was cancelled.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to cancel attempt {AttemptId}", attemptId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not cancel the exam.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/exams/result/{attemptId:int}")]
    public async Task<IActionResult> Result(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.GetAttemptResultAsync(attemptId, cancellationToken);

            AiInsightDto? insight = null;
            if (result.Status == AttemptStatusDto.Graded)
            {
                try
                {
                    insight = await api.GetExamInsightAsync(attemptId, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not pre-fetch AI insight for attempt {AttemptId}", attemptId);
                }
            }

            return View(new ExamResultViewModel(result, TempData["ErrorMessage"] as string, insight));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Attempt {AttemptId} not found for result", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load result for attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load exam results.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/exams/result/{attemptId:int}/status")]
    public async Task<IActionResult> ResultStatus(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.GetAttemptResultAsync(attemptId, cancellationToken);
            return Json(new
            {
                status = result.Status.ToString(),
                isGraded = result.Status == AttemptStatusDto.Graded,
                totalScore = result.TotalScore,
                bandCode = result.BandCode,
                freeResponseCount = result.FreeResponseCount,
                gradedFreeResponseCount = result.GradedFreeResponseCount,
                failedFreeResponseCount = result.FailedFreeResponseCount
            });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to poll grading status for attempt {AttemptId}", attemptId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpGet("/exams/insight/{attemptId:int}")]
    public async Task<IActionResult> GetInsight(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var insight = await api.GetExamInsightAsync(attemptId, cancellationToken);
            return Ok(insight);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/exams/insight/{attemptId:int}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryInsight(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            await api.RetryExamInsightAsync(attemptId, cancellationToken);
            return Ok(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpGet("/exams/history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        try
        {
            var history = await api.GetExamHistoryAsync(cancellationToken);
            return View(new ExamHistoryViewModel(history, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load exam history");
            TempData["ShellError"] = "Could not load exam history. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/exams/review/{attemptId:int}")]
    public async Task<IActionResult> Review(int attemptId, CancellationToken cancellationToken)
    {
        try
        {
            var review = await api.GetAttemptReviewAsync(attemptId, cancellationToken);
            return View(new ExamReviewViewModel(review, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.AttemptNotFound)
        {
            logger.LogWarning(ex, "Attempt {AttemptId} not found for review", attemptId);
            Response.StatusCode = 404;
            return View("AttemptNotFound");
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.AttemptNotFinished)
        {
            logger.LogWarning(ex, "Attempt {AttemptId} is not finished for review", attemptId);
            TempData["ShellError"] = "Review is only available for completed exam attempts.";
            return RedirectToAction(nameof(Take), new { attemptId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load review for attempt {AttemptId}", attemptId);
            TempData["ShellError"] = "Could not load exam review.";
            return RedirectToAction(nameof(Index));
        }
    }
}
