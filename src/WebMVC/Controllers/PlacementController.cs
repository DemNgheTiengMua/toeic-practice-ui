using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;
using WebMVC.Mappers;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Learner)]
public sealed class PlacementController(
    IPlacementClient api,
    CurrentUser currentUser,
    ILogger<PlacementController> logger,
    WebMVC.Formatters.DateTimeFormatter dates,
    ILearningPathClient? learningPathClient = null,
    IAiInsightsClient? insightsClient = null)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/placement")]
    [HttpGet("/placement/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await api.GetPlacementCatalogAsync(cancellationToken);
            return View(new PlacementSelectViewModel(catalog, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load placement test catalog");
            TempData["ShellError"] = "Could not load the placement tests. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpPost("/placement/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromForm] int examId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await api.StartPlacementTestAsync(new StartPlacementTestRequest(examId), cancellationToken);
            return RedirectToAction(nameof(Take), new { placementId = response.PlacementId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.ExamNotFound)
        {
            logger.LogWarning(ex, "Placement exam {ExamId} not found", examId);
            TempData["ShellError"] = "The requested placement exam does not exist.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to start placement test for exam {ExamId}", examId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start placement test. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/placement/take/{placementId:int}")]
    public async Task<IActionResult> Take(int placementId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            PlacementTakeViewDto take;
            try
            {
                take = await api.GetPlacementTakeAsync(placementId, q, cancellationToken);
            }
            catch (ApiException ex) when (ex.Code == ApiError.Codes.PreflightRequired)
            {
                var preflight = await api.GetPlacementPreflightStatusAsync(placementId, cancellationToken);
                return RedirectToAction(nameof(ExamsController.Preflight), "Exams", new { attemptId = preflight.AttemptId, placementId });
            }

            if (take.Status == PlacementStatusDto.Done || take.IsAutoSubmitted)
            {
                return RedirectToAction(nameof(Result), new { placementId });
            }

            if (take.Status == PlacementStatusDto.Abandoned)
            {
                TempData["ErrorMessage"] = "This placement test has been abandoned.";
                return RedirectToAction(nameof(Index));
            }

            if (q.HasValue && take.CurrentQuestion is not null && take.CurrentQuestion.DisplayOrder > 0 && take.CurrentQuestion.DisplayOrder != q.Value)
            {
                return RedirectToAction(nameof(Take), new { placementId, q = take.CurrentQuestion.DisplayOrder });
            }

            return View(new PlacementTakeViewModel(take, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PlacementExpired)
        {
            logger.LogInformation("Placement test {PlacementId} expired and was auto-submitted", placementId);
            return RedirectToAction(nameof(Result), new { placementId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.PlacementNotFound)
        {
            logger.LogWarning(ex, "Placement test {PlacementId} not found", placementId);
            Response.StatusCode = 404;
            return View("PlacementNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load placement test {PlacementId}", placementId);
            TempData["ShellError"] = "Could not load the placement test.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/placement/take/{placementId:int}/fragment")]
    public async Task<IActionResult> TakeFragment(int placementId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            var take = await api.GetPlacementTakeAsync(placementId, q, cancellationToken);
            if (take.Status == PlacementStatusDto.Done || take.IsAutoSubmitted)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { redirectUrl = Url.Action(nameof(Result), new { placementId }) });
            }

            return PartialView("_TakeDynamic", new PlacementTakeViewModel(take));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PreflightRequired)
        {
            // Preflight resolves its attempt from attemptId; sending only placementId 404s and
            // bounces the candidate out to /exams.
            var preflight = await api.GetPlacementPreflightStatusAsync(placementId, cancellationToken);
            return StatusCode(StatusCodes.Status409Conflict,
                new { redirectUrl = Url.Action(nameof(ExamsController.Preflight), "Exams", new { attemptId = preflight.AttemptId, placementId }) });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load placement fragment {PlacementId}", placementId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpPost("/placement/take/{placementId:int}/answers/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAnswer(
        int placementId,
        int questionId,
        [FromForm] SaveAnswerFormModel form,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new SaveAnswerRequest(form.SelectedOptionId, form.IsFlagged, form.ResponseText);
            var result = await api.SavePlacementAnswerAsync(placementId, questionId, request, cancellationToken);

            return Json(new
            {
                questionId = result.QuestionId,
                selectedOptionId = result.SelectedOptionId,
                isFlagged = result.IsFlagged,
                remainingSeconds = result.RemainingSeconds,
                isAutoSubmitted = result.IsAutoSubmitted,
                redirectUrl = result.IsAutoSubmitted ? Url.Action(nameof(Result), new { placementId }) : null
            });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PlacementExpired)
        {
            logger.LogInformation("Placement test {PlacementId} expired during save answer for question {QuestionId}", placementId, questionId);
            return Json(new
            {
                isAutoSubmitted = true,
                redirectUrl = Url.Action(nameof(Result), new { placementId })
            });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save answer for placement test {PlacementId}, question {QuestionId}", placementId, questionId);
            return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("/placement/take/{placementId:int}/draft")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(
        int placementId,
        [FromBody] SavePlacementDraftRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.SavePlacementDraftAsync(placementId, request, cancellationToken);
            return Ok(result);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save placement draft for placement {PlacementId}, question {QuestionId}", placementId, request.QuestionId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpPost("/placement/take/{placementId:int}/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int placementId, [FromForm] bool isAutoSubmit = false, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.SubmitPlacementTestAsync(placementId, new SubmitPlacementTestRequest(isAutoSubmit), cancellationToken);
            return RedirectToAction(nameof(Result), new { placementId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PlacementExpired || ex.Code == ApiError.Codes.PlacementNotActive)
        {
            logger.LogInformation(ex, "Placement test {PlacementId} expired or already completed during submit", placementId);
            return RedirectToAction(nameof(Result), new { placementId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to submit placement test {PlacementId}", placementId);
            TempData["ErrorMessage"] = ex.Detail ?? "Failed to submit placement test. Please try again.";
            return RedirectToAction(nameof(Take), new { placementId });
        }
    }

    [HttpGet("/placement/result/{placementId:int}")]
    public async Task<IActionResult> Result(int placementId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.GetPlacementResultAsync(placementId, cancellationToken);

            AiInsightDto? insight = null;
            if (result.IsGraded && insightsClient != null)
            {
                try
                {
                    insight = await insightsClient.GetExamInsightAsync(result.AttemptId, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not pre-fetch AI insight for placement attempt {AttemptId}", result.AttemptId);
                }
            }

            return View(AttemptViewModelMapper.PlacementResult(result, dates, TempData["ErrorMessage"] as string, insight));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PlacementNotFinished)
        {
            logger.LogInformation("Placement test {PlacementId} is not finished yet, redirecting to take", placementId);
            return RedirectToAction(nameof(Take), new { placementId });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.PlacementNotFound)
        {
            logger.LogWarning(ex, "Placement test {PlacementId} not found for result", placementId);
            Response.StatusCode = 404;
            return View("PlacementNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load result for placement test {PlacementId}", placementId);
            TempData["ShellError"] = "Could not load placement test results.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/placement/result/{placementId:int}/status")]
    public async Task<IActionResult> Status(int placementId, CancellationToken cancellationToken)
    {
        try
        {
            var status = await api.GetPlacementGradingStatusAsync(placementId, cancellationToken);
            return Json(status);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to get placement grading status for placement {PlacementId}", placementId);
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Detail ?? ex.Message });
        }
    }

    [HttpGet("/placement/target-band")]
    public async Task<IActionResult> TargetBand(CancellationToken cancellationToken)
    {
        try
        {
            var targetView = await api.GetTargetBandViewAsync(cancellationToken);
            return View(AttemptViewModelMapper.TargetBand(
                targetView,
                TempData["ErrorMessage"] as string,
                TempData["SuccessMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load target band view");
            TempData["ShellError"] = "Could not load target bands. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/placement/target-band")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TargetBand([FromForm] SetTargetBandFormModel form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.TargetBandCode))
        {
            TempData["ErrorMessage"] = "Please select a target level.";
            return RedirectToAction(nameof(TargetBand));
        }

        try
        {
            await api.SetTargetBandAsync(new SetTargetBandRequest(form.TargetBandCode), cancellationToken);

            if (learningPathClient != null)
            {
                try
                {
                    await learningPathClient.GenerateLearningPathAsync(new GenerateLearningPathRequest(form.TargetBandCode, ArchiveExisting: true), cancellationToken);
                }
                catch (ApiException ex)
                {
                    logger.LogWarning(ex, "Could not auto-generate learning path after setting target band");
                }

                TempData["SuccessMessage"] = "Your target level has been saved and your learning path is ready!";
                return RedirectToAction("Index", "LearningPath");
            }

            TempData["SuccessMessage"] = "Your target level has been saved.";
            return RedirectToAction(nameof(TargetBand));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to set target band to {TargetBand}", form.TargetBandCode);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not save target level. Please try again.";
            return RedirectToAction(nameof(TargetBand));
        }
    }
}
