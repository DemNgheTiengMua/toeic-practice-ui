using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{

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
