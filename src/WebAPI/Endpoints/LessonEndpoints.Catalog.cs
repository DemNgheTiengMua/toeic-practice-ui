using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{

    private static async Task<IResult> GetLessonAsync(
        int id,
        string? module,
        HttpContext http,
        ILearnerService learners,
        ILessonService lessons,
        CancellationToken cancellationToken)
    {
        try
        {
            var certId = await learners.GetActiveCertIdAsync(CurrentUserId(http), cancellationToken);
            var lesson = await lessons.GetLessonAsync(id, certId, ModuleOrDefault(module));
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

    /// <summary>#240: the lesson addressed by a cell, so a caller that knows (skill, topic, level) -
    /// a path step's coordinate - never has to guess which lesson of the skill it means.</summary>
    private static async Task<IResult> GetLessonForCellAsync(
        int skillId,
        int topicId,
        string level,
        string? module,
        HttpContext http,
        ILearnerService learners,
        ILessonService lessons,
        CancellationToken cancellationToken)
    {
        var certId = await learners.GetActiveCertIdAsync(CurrentUserId(http), cancellationToken);
        var lesson = await lessons.GetLessonForCellAsync(
            certId, skillId, topicId, level, ModuleOrDefault(module));

        return lesson is null
            ? Problem(StatusCodes.Status404NotFound, "No lesson for this cell", ApiError.Codes.LessonNotFound)
            : Results.Ok(lesson);
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
}
