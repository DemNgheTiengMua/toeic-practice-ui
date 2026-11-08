using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class PracticeController(
    IPracticeClient api,
    CurrentUser currentUser,
    ILogger<PracticeController> logger,
    WebMVC.Formatters.DateTimeFormatter dates,
    IPlacementClient? placementClient = null)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/practice")]
    [HttpGet("/practice/index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await api.GetPracticeCatalogAsync(cancellationToken);

            // #235: the lock is an addition to the page, never a precondition — a failure to read it
            // must not hide the drills the learner can start.
            FreeDrillStateDto? freeDrill = null;
            try
            {
                freeDrill = await api.GetFreeDrillStateAsync(null, null, cancellationToken);
            }
            catch (ApiException ex)
            {
                logger.LogWarning(ex, "Could not read the free-drill state");
            }

            // #238: the objective practice matrix. A failure to read it is an addition to the page, never
            // a precondition — the drill the learner can start must not disappear because the matrix
            // could not be counted.
            IReadOnlyList<PracticeCellDto> cells = [];
            try
            {
                cells = await api.GetPracticeMatrixAsync(cancellationToken);
            }
            catch (ApiException ex)
            {
                logger.LogWarning(ex, "Could not read the objective practice matrix");
            }

            return View(new PracticeCatalogViewModel(catalog, TempData["ErrorMessage"] as string)
            {
                FreeDrill = freeDrill,
                UnfinishedStartedAtDisplay = catalog.UnfinishedSession is { } unfinished ? dates.FormatDate(unfinished.StartedAt) : "",
                // Every card that shows a cooldown needs its instant in the learner's own zone. The
                // view used to print it as UTC, which is 7 hours wrong for a Vietnamese learner.
                NextReviewDueDisplay = catalog.ListeningSections
                    .Concat(catalog.ReadingSections)
                    .Concat(catalog.OtherSections)
                    .Where(s => s.NextReviewDueAt is not null)
                    .ToDictionary(s => s.SectionId, s => dates.FormatCustom(s.NextReviewDueAt!.Value, "HH:mm dd MMM")),
                // #238: a resting cell's reopen instant is converted here, in the learner's zone, so the
                // matrix never repeats #235's UTC-boundary error. The DST-less offset (UTC+7) is exact;
                // see the report for the zone this does and does not cover.
                Matrix = cells.Select(c => new PracticeMatrixRowViewModel(
                    c.TopicCode,
                    c.TopicName,
                    CefrLevelLabels.LabelForCode(c.LevelCode, logger),
                    c.Mastery.ToString(),
                    PracticeMatrixRowViewModel.LabelFor(c.Mastery.ToString()),
                    c.IsPrioritised,
                    c.IsRestingNow,
                    c.ReopensAt is { } reopens ? dates.FormatCustom(reopens, "HH:mm dd MMM") : null)).ToList()
            });
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
            var request = new StartPracticeRequest(form.SectionId);
            var session = await api.StartPracticeSessionAsync(request, cancellationToken);
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
            var session = await api.GetPracticeSessionInfoAsync(sessionId, cancellationToken);

            if (session.Status == PracticeStatusDto.Finished && !q.HasValue)
            {
                return RedirectToAction(nameof(Summary), new { sessionId });
            }

            var questions = await api.GetPracticeSessionQuestionsAsync(sessionId, cancellationToken);

            PracticeQuestionDto question;
            var displayOrder = q.GetValueOrDefault(0);

            if (questions != null && questions.Count > 0)
            {
                if (displayOrder <= 0)
                {
                    question = questions.FirstOrDefault(x => !IsAnswered(x))
                               ?? questions[0];
                }
                else
                {
                    question = questions.FirstOrDefault(x => x.DisplayOrder == displayOrder)
                               ?? questions[0];
                }
            }
            else
            {
                question = await api.GetPracticeQuestionAsync(sessionId, displayOrder, cancellationToken);
                questions = [question];
            }

            var navigator = questions.Select(item => new PracticeNavigatorItemDto(
                item.QuestionId,
                item.DisplayOrder,
                IsAnswered(item),
                item.IsCorrect,
                item.IsSaved,
                item.DisplayOrder == question.DisplayOrder,
                item.ItemVersionId,
                item.SavedQuestionId
            )).ToList();

            return View(new PracticeTakeViewModel(
                session,
                question,
                TempData["ErrorMessage"] as string,
                questions,
                navigator));
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
            var result = await api.RevealPracticeHintAsync(sessionId, questionId, cancellationToken);
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
            var isSkip = form.Action == "skip";
            var selectedOptionId = isSkip ? null : form.SelectedOptionId;
            var responseText = isSkip ? null : form.ResponseText;
            var selectedOptionIdsJson = isSkip ? null : form.SelectedOptionIdsJson;
            var selectedOptionIds = isSkip ? null : form.SelectedOptionIds;
            var result = await api.SubmitPracticeAnswerAsync(
                sessionId, questionId, new SubmitPracticeAnswerRequest(selectedOptionId, responseText, selectedOptionIdsJson, selectedOptionIds), cancellationToken);

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
                    isSaved = result.IsSaved,
                    acceptedAnswersJson = result.AcceptedAnswersJson,
                    correctOptionIds = result.CorrectOptionIds,
                    responseText = result.ResponseText,
                    selectedOptionIdsJson = result.SelectedOptionIdsJson,
                    savedQuestionId = result.SavedQuestionId,
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

    /// <summary>#253: the DTO's answer columns through the one shared definition.</summary>
    private static bool IsAnswered(PracticeQuestionDto question) =>
        AnswerContent.IsAnswered(
            question.SelectedOptionId, null, question.ResponseText, question.SelectedOptionIdsJson);

    /// <summary>#156: the calibration view is an addition to a page, never a precondition: a failure
    /// to read it must not hide the summary the learner just earned.</summary>
    private async Task<CalibrationViewDto?> TryGetCalibrationAsync(CancellationToken cancellationToken)
    {
        if (placementClient is null)
        {
            return null;
        }

        try
        {
            return await placementClient.GetCalibrationAsync(cancellationToken);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Could not read calibration state for the practice summary");
            return null;
        }
    }

    [HttpGet("/practice/{sessionId:int}/summary")]
    public async Task<IActionResult> Summary(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await api.GetPracticeSummaryAsync(sessionId, cancellationToken);

            AiInsightDto? insight = null;
            try
            {
                insight = await api.GetPracticeInsightAsync(sessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not pre-fetch AI insight for practice session {SessionId}", sessionId);
            }
            return View(new PracticeSummaryViewModel(summary, dates.Format(summary.StartedAt))
            {
                InsightPanel = new("practice_session", summary.SessionId, insight),
                // #156: the drill is exactly where the divergence becomes visible, so the proposal
                // is shown here too rather than only on the path screen the learner may never open.
                Calibration = await TryGetCalibrationAsync(cancellationToken)
            });
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
            var insight = await api.GetPracticeInsightAsync(sessionId, cancellationToken);
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
            await api.RetryPracticeInsightAsync(sessionId, cancellationToken);
            return Ok(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }
}
