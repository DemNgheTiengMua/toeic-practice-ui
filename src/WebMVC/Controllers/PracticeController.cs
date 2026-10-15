using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class PracticeController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<PracticeController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/practice")]
    [HttpGet("/practice/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var catalog = await Api.GetPracticeCatalogAsync(bearer, cancellationToken);
            return View(new PracticeCatalogViewModel(catalog, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load practice catalog");
            TempData["ShellError"] = "Could not load the practice catalog. Please try again.";
            return RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpPost("/practice/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(StartPracticeFormModel form, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var request = new StartPracticeRequest(form.SectionId, form.QuestionCount, form.QuestionSource, form.FeedbackMode);
            var session = await Api.StartPracticeSessionAsync(request, bearer, cancellationToken);
            return RedirectToAction(nameof(Take), new { sessionId = session.SessionId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to start practice session for section {SectionId}", form.SectionId);
            TempData["ErrorMessage"] = ex.Detail ?? "Could not start practice session. Please select another section or try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/practice/{sessionId:int}")]
    public async Task<IActionResult> Take(int sessionId, [FromQuery] int? q, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var session = await Api.GetPracticeSessionInfoAsync(sessionId, bearer, cancellationToken);

            if (session.Status == PracticeStatusDto.Finished && !q.HasValue)
            {
                return RedirectToAction(nameof(Summary), new { sessionId });
            }

            var displayOrder = q.GetValueOrDefault(0);
            var question = await Api.GetPracticeQuestionAsync(sessionId, displayOrder, bearer, cancellationToken);

            return View(new PracticeTakeViewModel(session, question, TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.PracticeSessionNotFound || ex.Code == ApiError.Codes.PracticeQuestionNotFound)
        {
            logger.LogWarning(ex, "Practice session or question not found: {SessionId}", sessionId);
            Response.StatusCode = 404;
            return View("SessionNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load practice session {SessionId}", sessionId);
            TempData["ErrorMessage"] = "Could not load practice session. Check your connection and try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/practice/{sessionId:int}/questions/{questionId:int}/hint")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hint(int sessionId, int questionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var result = await Api.RevealPracticeHintAsync(sessionId, questionId, bearer, cancellationToken);
            return Json(new { hint = result.Hint, hintUsed = result.HintUsed });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to reveal hint for question {QuestionId} in session {SessionId}", questionId, sessionId);
            return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    [HttpPost("/practice/{sessionId:int}/questions/{questionId:int}/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Answer(
        int sessionId,
        int questionId,
        [FromForm] SubmitAnswerFormModel form,
        [FromQuery] int? displayOrder,
        CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var isSkip = form.Action == "skip";
            var selectedOptionId = isSkip ? null : form.SelectedOptionId;
            var result = await Api.SubmitPracticeAnswerAsync(
                sessionId, questionId, new SubmitPracticeAnswerRequest(selectedOptionId), bearer, cancellationToken);

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    isCorrect = result.IsCorrect,
                    selectedOptionId = result.SelectedOptionId,
                    correctOptionId = result.CorrectOptionId,
                    explanation = result.Explanation,
                    transcript = result.Transcript,
                    isSessionFinished = result.IsSessionFinished,
                    nextQuestionDisplayOrder = result.NextQuestionDisplayOrder,
                });
            }

            if (isSkip)
            {
                if (result.IsSessionFinished)
                {
                    return RedirectToAction(nameof(Summary), new { sessionId });
                }

                var nextOrder = result.NextQuestionDisplayOrder ?? (displayOrder.GetValueOrDefault(1) + 1);
                return RedirectToAction(nameof(Take), new { sessionId, q = nextOrder });
            }

            // For non-AJAX answer submit: reload this question so the explanation and correctness are displayed
            return RedirectToAction(nameof(Take), new { sessionId, q = displayOrder.GetValueOrDefault(1) });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to submit answer for question {QuestionId} in session {SessionId}", questionId, sessionId);

            if (IsAjaxRequest())
            {
                return StatusCode(ex.Status, new { error = ex.Detail ?? ex.Message, code = ex.Code });
            }

            if (ex.Status == 404 || ex.Code == ApiError.Codes.PracticeSessionNotFound)
            {
                Response.StatusCode = 404;
                return View("SessionNotFound");
            }

            TempData["ErrorMessage"] = ex.Detail ?? "Could not submit your answer. Please try again.";
            return RedirectToAction(nameof(Take), new { sessionId, q = displayOrder });
        }
    }

    private bool IsAjaxRequest() =>
        Request.Headers.Accept.ToString().Contains("application/json")
        || string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    [HttpGet("/practice/{sessionId:int}/summary")]
    public async Task<IActionResult> Summary(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var summary = await Api.GetPracticeSummaryAsync(sessionId, bearer, cancellationToken);

            AiInsightDto? insight = null;
            try
            {
                insight = await Api.GetPracticeInsightAsync(sessionId, bearer, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not pre-fetch AI insight for practice session {SessionId}", sessionId);
            }
            ViewBag.Insight = insight;

            return View(summary);
        }
        catch (ApiException ex) when (ex.Status == 404 || ex.Code == ApiError.Codes.PracticeSessionNotFound)
        {
            logger.LogWarning(ex, "Practice summary for session {SessionId} not found", sessionId);
            Response.StatusCode = 404;
            return View("SessionNotFound");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load practice summary for session {SessionId}", sessionId);
            TempData["ErrorMessage"] = "Could not load practice summary.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("/practice/insight/{sessionId:int}")]
    public async Task<IActionResult> GetInsight(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var insight = await Api.GetPracticeInsightAsync(sessionId, bearer, cancellationToken);
            return Ok(insight);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/practice/insight/{sessionId:int}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryInsight(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            await Api.RetryPracticeInsightAsync(sessionId, bearer, cancellationToken);
            return Ok(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }
}
