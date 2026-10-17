using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class LessonEndpoints
{
    public static void MapLessonEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // GET /api/learner/lessons/{id:int}
        learner.MapGet("/lessons/{id:int}", GetLessonAsync);

        // GET /api/learner/lessons/{id:int}/progress
        learner.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);

        // POST /api/learner/lessons/{id:int}/cards/{cardId:int}/view
        learner.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);

        // GET /api/learner/lessons/by-category/{categoryId:int}
        learner.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);

        // POST /api/learner/learning-path/{pathId:int}/unlock
        learner.MapPost("/learning-path/{pathId:int}/unlock", UnlockPathAsync);

        // Direct aliases without /api/learner prefix
        var direct = app.MapGroup("").RequireAuthorization(Roles.Learner);
        direct.MapGet("/lessons/{id:int}", GetLessonAsync);
        direct.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);
        direct.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);
        direct.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);
        direct.MapPost("/learning-path/{pathId:int}/unlock", UnlockPathAsync);
    }

    private static async Task<IResult> GetLessonAsync(
        int id,
        HttpContext http,
        ILearnerService learners,
        ILessonService lessons,
        CancellationToken cancellationToken)
    {
        try
        {
            var certId = await learners.GetActiveCertIdAsync(CurrentUserId(http), cancellationToken);
            var lesson = await lessons.GetLessonAsync(id, certId);
            if (lesson is null)
            {
                return Problem(StatusCodes.Status404NotFound, "Lesson not found", ApiError.Codes.LessonNotFound);
            }
            return Results.Ok(lesson);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(StatusCodes.Status404NotFound, "Lesson not found", ApiError.Codes.LessonNotFound, ex.Message);
        }
    }

    private static async Task<IResult> GetLessonProgressAsync(
        int id,
        HttpContext http,
        ILessonService lessons)
    {
        var progress = await lessons.GetProgressAsync(CurrentUserId(http), id);
        return Results.Ok(progress);
    }

    private static async Task<IResult> RecordCardViewAsync(
        int id,
        int cardId,
        HttpContext http,
        ILessonService lessons)
    {
        await lessons.RecordCardViewAsync(CurrentUserId(http), id, cardId);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetLessonByCategoryAsync(
        int categoryId,
        HttpContext http,
        ILearnerService learners,
        ILessonService lessons,
        CancellationToken cancellationToken)
    {
        var certId = await learners.GetActiveCertIdAsync(CurrentUserId(http), cancellationToken);
        var lesson = await lessons.GetLessonByCategoryAsync(categoryId, certId);
        if (lesson is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Lesson not found for category", ApiError.Codes.LessonNotFound);
        }
        return Results.Ok(lesson);
    }

    private static async Task<IResult> UnlockPathAsync(
        int pathId,
        HttpContext http,
        IPremiumPathService premiumPaths)
    {
        try
        {
            await premiumPaths.UnlockPathAsync(CurrentUserId(http), pathId);
            return Results.Ok(new { success = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient credits", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status400BadRequest, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, "Cannot unlock path", ApiError.Codes.Unexpected, ex.Message);
        }
    }
}
