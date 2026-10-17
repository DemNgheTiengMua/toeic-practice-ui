using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Learner)]
public sealed class LessonsController(
    ILessonClient api,
    CurrentUser currentUser,
    ILogger<LessonsController> logger,
    ILearnerContextClient? learnerContext = null)
    : LearnerControllerBase(currentUser, logger, learnerContext)
{
    [HttpGet("/lessons/{id:int}")]
    public async Task<IActionResult> Index(int id, [FromQuery] int? moduleId, CancellationToken cancellationToken)
    {
        try
        {
            var lesson = await api.GetLessonAsync(id, cancellationToken);
            var progress = await api.GetLessonProgressAsync(id, cancellationToken);
            var model = new LessonViewModel(lesson, progress, moduleId);
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
    public async Task<IActionResult> ByCategory(int categoryId, [FromQuery] int? moduleId, CancellationToken cancellationToken)
    {
        try
        {
            var lesson = await api.GetLessonByCategoryAsync(categoryId, cancellationToken);
            if (lesson is null)
            {
                TempData["ShellError"] = "No lesson found for this topic yet.";
                return RedirectToAction("Index", "LearningPath");
            }
            return RedirectToAction(nameof(Index), new { id = lesson.LessonId, moduleId });
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

    [HttpPost("/learning-path/{pathId:int}/unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockPath(int pathId, CancellationToken cancellationToken)
    {
        try
        {
            await api.UnlockPathAsync(pathId, cancellationToken);
            return Json(new { success = true });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to unlock path {PathId}", pathId);
            return StatusCode(StatusCodes.Status400BadRequest, new { error = "Insufficient credits to unlock path.", code = ApiError.Codes.InsufficientCredits });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to unlock path {PathId}", pathId);
            return StatusCode(ex.Status, new { error = ex.Message, code = ex.Code });
        }
    }
}
