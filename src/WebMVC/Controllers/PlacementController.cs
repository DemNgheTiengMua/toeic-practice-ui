using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Learner)]
public sealed class PlacementController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<PlacementController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/placement")]
    [HttpGet("/placement/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var catalog = await Api.GetPlacementCatalogAsync(bearer, cancellationToken);
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
            var bearer = await BearerAsync();
            var response = await Api.StartPlacementTestAsync(new StartPlacementTestRequest(examId), bearer, cancellationToken);
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
            var bearer = await BearerAsync();
            var take = await Api.GetPlacementTakeAsync(placementId, q, bearer, cancellationToken);

            if (take.Status == PlacementStatusDto.Done || take.IsAutoSubmitted)
            {
                return RedirectToAction(nameof(Result), new { placementId });
            }

            if (take.Status == PlacementStatusDto.Abandoned)
            {
                TempData["ErrorMessage"] = "This placement test has been abandoned.";
                return RedirectToAction(nameof(Index));
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
            var bearer = await BearerAsync();
            var request = new SaveAnswerRequest(form.SelectedOptionId, form.IsFlagged);
            var result = await Api.SavePlacementAnswerAsync(placementId, questionId, request, bearer, cancellationToken);

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

    [HttpPost("/placement/take/{placementId:int}/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int placementId, [FromForm] bool isAutoSubmit = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.SubmitPlacementTestAsync(placementId, new SubmitPlacementTestRequest(isAutoSubmit), bearer, cancellationToken);
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
            var bearer = await BearerAsync();
            var result = await Api.GetPlacementResultAsync(placementId, bearer, cancellationToken);
            return View(new PlacementResultViewModel(result, TempData["ErrorMessage"] as string));
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

    [HttpGet("/placement/target-band")]
    public async Task<IActionResult> TargetBand(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var targetView = await Api.GetTargetBandViewAsync(bearer, cancellationToken);
            return View(new TargetBandViewModel(
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
            var bearer = await BearerAsync();
            await Api.SetTargetBandAsync(new SetTargetBandRequest(form.TargetBandCode), bearer, cancellationToken);
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
