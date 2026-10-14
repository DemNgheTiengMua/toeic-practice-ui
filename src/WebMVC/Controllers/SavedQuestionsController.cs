using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class SavedQuestionsController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<SavedQuestionsController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    private bool IsAjaxRequest =>
        Request.Headers.XRequestedWith == "XMLHttpRequest" ||
        Request.Headers.Accept.ToString().Contains("application/json");

    [HttpGet("/saved-questions")]
    [HttpGet("/saved-questions/index")]
    public async Task<IActionResult> Index(
        [FromQuery] string? source,
        [FromQuery] int? sectionId,
        [FromQuery] int? categoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var data = await Api.GetSavedQuestionsAsync(source, sectionId, categoryId, bearer, cancellationToken);
            return View(new SavedQuestionsIndexViewModel(data, source, sectionId, categoryId, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load saved questions");
            TempData["ShellError"] = "Could not load saved questions. Please check your connection and try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet("/saved-questions/{questionId:int}")]
    public async Task<IActionResult> Review(int questionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var question = await Api.GetSavedQuestionAsync(questionId, bearer, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(question);
            }

            return View(new SavedQuestionReviewViewModel(question, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.SavedQuestionNotFound)
        {
            logger.LogWarning(ex, "Saved question not found: {QuestionId}", questionId);
            if (IsAjaxRequest)
            {
                return StatusCode(404, new { error = "Question not found in your saved bank.", code = ex.Code });
            }
            TempData["ErrorMessage"] = "This question is no longer in your saved bank.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load saved question review: {QuestionId}", questionId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = "Could not load question details. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/saved-questions/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int questionId, [FromForm] string? source, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var res = await Api.SaveQuestionAsync(questionId, source ?? "manual", bearer, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(new { isSaved = res.IsSaved, source = res.Source, savedAt = res.SavedAt });
            }

            TempData["SuccessMessage"] = "Question saved to your bank.";
            return RedirectBackOr(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save question {QuestionId}", questionId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = ex.Detail ?? "Could not save question. Please try again.";
            return RedirectBackOr(nameof(Index));
        }
    }

    [HttpPost("/saved-questions/{questionId:int}/remove")]
    [HttpDelete("/saved-questions/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int questionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var res = await Api.UnsaveQuestionAsync(questionId, bearer, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(new { isSaved = false });
            }

            TempData["SuccessMessage"] = "Question removed from your bank.";
            return RedirectBackOr(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to remove saved question {QuestionId}", questionId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = ex.Detail ?? "Could not remove question. Please try again.";
            return RedirectBackOr(nameof(Index));
        }
    }

    [HttpPost("/saved-questions/{questionId:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int questionId, [FromForm] bool? currentlySaved, [FromForm] string? source, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            SaveQuestionResultDto res;
            if (currentlySaved == true)
            {
                res = await Api.UnsaveQuestionAsync(questionId, bearer, cancellationToken);
            }
            else
            {
                res = await Api.SaveQuestionAsync(questionId, source ?? "manual", bearer, cancellationToken);
            }

            return Json(new { isSaved = res.IsSaved, source = res.Source, savedAt = res.SavedAt });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to toggle save state for question {QuestionId}", questionId);
            return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    private IActionResult RedirectBackOr(string actionName)
    {
        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            return Redirect(uri.PathAndQuery);
        }
        return RedirectToAction(actionName);
    }
}
