using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #224: a bookmark is addressed by its own row id (<c>savedQuestionId</c>), and saving is two routes
/// because a legacy question id and a pool item-version id are separate counters. Each route is named
/// for the shape it accepts, so a pool address can never be posted to a route that treats it as a
/// question id.
/// </summary>
public sealed class SavedQuestionsController(
    ISavedQuestionsClient api,
    CurrentUser currentUser,
    ILogger<SavedQuestionsController> logger,
    WebMVC.Formatters.DateTimeFormatter dates)
    : LearnerControllerBase(currentUser, logger)
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
            var data = await api.GetSavedQuestionsAsync(source, sectionId, categoryId, cancellationToken);
            return View(new SavedQuestionsIndexViewModel(data, source, sectionId, categoryId, TempData["ErrorMessage"] as string)
            {
                SavedAtDisplays = data.Questions.ToDictionary(q => q.SavedQuestionId, q => dates.Format(q.SavedAt))
            });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load saved questions");
            TempData["ShellError"] = "Could not load saved questions. Please check your connection and try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet("/saved-questions/{savedQuestionId:int}")]
    public async Task<IActionResult> Review(int savedQuestionId, CancellationToken cancellationToken)
    {
        try
        {
            var question = await api.GetSavedQuestionAsync(savedQuestionId, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(question);
            }

            return View(new SavedQuestionReviewViewModel(question, TempData["ErrorMessage"] as string)
            {
                SavedAtDisplay = dates.Format(question.SavedAt)
            });
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.SavedQuestionNotFound)
        {
            logger.LogWarning(ex, "Saved question not found: {SavedQuestionId}", savedQuestionId);
            if (IsAjaxRequest)
            {
                return StatusCode(404, new { error = "Question not found in your saved bank.", code = ex.Code });
            }
            TempData["ErrorMessage"] = "This question is no longer in your saved bank.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load saved question review: {SavedQuestionId}", savedQuestionId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = "Could not load question details. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>Saves a legacy question-shaped link.</summary>
    [HttpPost("/saved-questions/questions/{questionId:int}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Save(int questionId, [FromForm] string? source, CancellationToken cancellationToken) =>
        SaveCore(() => api.SaveQuestionAsync(questionId, source ?? "manual", cancellationToken), questionId, cancellationToken);

    /// <summary>#224: saves a pool item, pinning the version that was on screen.</summary>
    [HttpPost("/saved-questions/item-versions/{itemVersionId:int}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveItemVersion(int itemVersionId, [FromForm] string? source, CancellationToken cancellationToken) =>
        SaveCore(() => api.SaveItemVersionAsync(itemVersionId, source ?? "manual", cancellationToken), itemVersionId, cancellationToken);

    [HttpPost("/saved-questions/{savedQuestionId:int}/remove")]
    [HttpDelete("/saved-questions/{savedQuestionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int savedQuestionId, CancellationToken cancellationToken)
    {
        try
        {
            await api.UnsaveQuestionAsync(savedQuestionId, cancellationToken);

            if (IsAjaxRequest)
            {
                return Json(new { isSaved = false });
            }

            TempData["SuccessMessage"] = "Question removed from your bank.";
            return RedirectBackOr(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to remove saved question {SavedQuestionId}", savedQuestionId);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = ex.Detail ?? "Could not remove question. Please try again.";
            return RedirectBackOr(nameof(Index));
        }
    }

    /// <summary>
    /// The practice page's Save control. Unsaving addresses the bookmark by its own row id; saving
    /// addresses the shape the drill is showing, which is exactly one of the two ids below.
    /// </summary>
    [HttpPost("/saved-questions/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(
        [FromForm] int? savedQuestionId,
        [FromForm] int? questionId,
        [FromForm] int? itemVersionId,
        [FromForm] bool? currentlySaved,
        [FromForm] string? source,
        CancellationToken cancellationToken)
    {
        try
        {
            SaveQuestionResultDto res;
            if (currentlySaved == true && savedQuestionId.HasValue)
            {
                res = await api.UnsaveQuestionAsync(savedQuestionId.Value, cancellationToken);
            }
            else if (itemVersionId.HasValue)
            {
                res = await api.SaveItemVersionAsync(itemVersionId.Value, source ?? "manual", cancellationToken);
            }
            else if (questionId.HasValue)
            {
                res = await api.SaveQuestionAsync(questionId.Value, source ?? "manual", cancellationToken);
            }
            else
            {
                return StatusCode(400, new { error = "A saved question, question or item version is required.", code = ApiError.Codes.Validation });
            }

            return Json(new { isSaved = res.IsSaved, source = res.Source, savedAt = res.SavedAt, savedQuestionId = res.SavedQuestionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to toggle save state for saved question {SavedQuestionId}", savedQuestionId);
            return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    private async Task<IActionResult> SaveCore(Func<Task<SaveQuestionResultDto>> save, int address, CancellationToken cancellationToken)
    {
        try
        {
            var res = await save();

            if (IsAjaxRequest)
            {
                return Json(new { isSaved = res.IsSaved, source = res.Source, savedAt = res.SavedAt, savedQuestionId = res.SavedQuestionId });
            }

            TempData["SuccessMessage"] = "Question saved to your bank.";
            return RedirectBackOr(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save question {Address}", address);
            if (IsAjaxRequest)
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }
            TempData["ErrorMessage"] = ex.Detail ?? "Could not save question. Please try again.";
            return RedirectBackOr(nameof(Index));
        }
    }

    private IActionResult RedirectBackOr(string actionName)
    {
        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer))
        {
            if (Uri.TryCreate(referer, UriKind.Absolute, out var uri))
            {
                var isSameHost = Request.Host.HasValue
                    ? string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

                if (isSameHost)
                {
                    var target = uri.PathAndQuery;
                    if (IsLocalUrl(target))
                    {
                        return Redirect(target);
                    }
                }
            }
            else if (IsLocalUrl(referer))
            {
                return Redirect(referer);
            }
        }
        return RedirectToAction(actionName);
    }

    private bool IsLocalUrl(string? url)
    {
        if (Url is not null)
        {
            return Url.IsLocalUrl(url);
        }

        return !string.IsNullOrEmpty(url) &&
               ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
                (url.Length > 1 && url[0] == '~' && url[1] == '/'));
    }
}
