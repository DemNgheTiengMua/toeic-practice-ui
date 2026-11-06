using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Infrastructure;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize]
public sealed class LessonsController(
    ILessonClient api,
    CurrentUser currentUser,
    ILogger<LessonsController> logger,
    ILearnerContextClient? learnerContext = null)
    : Controller
{
    private readonly CurrentUser _currentUser = currentUser;

    [HttpGet("/lessons/{id:int}")]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        if (User.IsStaff())
        {
            return Redirect($"/admin/lessons/{id}/preview");
        }

        try
        {
            if (learnerContext is not null && LearnerShell.Get(HttpContext) is null)
            {
                try
                {
                    var shell = await learnerContext.GetLearnerContextAsync(cancellationToken);
                    LearnerShell.Set(HttpContext, shell);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load learner shell for lesson player");
                }
            }

            var lesson = await api.GetLessonAsync(id, cancellationToken);
            var progress = await api.GetLessonProgressAsync(id, cancellationToken);
            var model = new LessonViewModel(lesson, progress);
            return View(model);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.LessonNotFound || ex.Status == 404)
        {
            logger.LogWarning(ex, "Lesson {LessonId} not found", id);
            TempData["ShellError"] = "Lesson not found or unavailable.";
            return RedirectToAction("Index", "LearningPath");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load lesson {LessonId}", id);
            TempData["ShellError"] = "Could not load lesson. Please try again.";
            return RedirectToAction("Index", "LearningPath");
        }
    }

    [HttpGet("/lessons/by-category/{categoryId:int}")]
    public async Task<IActionResult> ByCategory(int categoryId, CancellationToken cancellationToken)
    {
        if (User.IsStaff())
        {
            return Redirect("/admin/lessons");
        }

        try
        {
            var lesson = await api.GetLessonByCategoryAsync(categoryId, cancellationToken);
            if (lesson is null)
            {
                TempData["ShellError"] = "No lesson found for this topic yet.";
                return RedirectToAction("Index", "LearningPath");
            }
            return RedirectToAction(nameof(Index), new { id = lesson.LessonId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to find lesson for category {CategoryId}", categoryId);
            TempData["ShellError"] = "No lesson found for this topic yet.";
            return RedirectToAction("Index", "LearningPath");
        }
    }

    [HttpPost("/lessons/{id:int}/cards/{cardId:int}/view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordCardView(int id, int cardId, CancellationToken cancellationToken)
    {
        if (User.IsStaff())
        {
            return Json(new { success = true, preview = true });
        }

        try
        {
            await api.RecordCardViewAsync(id, cardId, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to record card view for lesson {LessonId}, card {CardId}", id, cardId);
            return StatusCode(ex.Status, new { error = ex.Message, code = ex.Code });
        }
    }

    /// <summary>#155: grades the micro-check on the server. The page no longer carries the right answer,
    /// so this is the only way to find out - and the answer therefore reaches the mastery machine.</summary>
    [HttpPost("/lessons/{id:int}/cards/{cardId:int}/micro-check")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitMicroCheck(
        int id, int cardId, [FromForm] string selectedOption, CancellationToken cancellationToken)
    {
        if (User.IsStaff())
        {
            return BadRequest(new { error = "A lesson preview does not grade." });
        }

        try
        {
            var result = await api.SubmitMicroCheckAsync(id, cardId, selectedOption, cancellationToken);
            return Json(new
            {
                isCorrect = result.IsCorrect,
                correctOption = result.CorrectOption,
                explanation = result.Explanation,
            });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to grade micro-check card {CardId} in lesson {LessonId}", cardId, id);
            return StatusCode(ex.Status, new { error = ex.Message, code = ex.Code });
        }
    }
}
