using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{

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
}
