using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{

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
