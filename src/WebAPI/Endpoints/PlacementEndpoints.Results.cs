using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementResultsEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/placement/result/{placementId:int}", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.GetPlacementResultAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(ToPlacementResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not finished", ApiError.Codes.PlacementNotFinished, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementAbandoned)
            {
                // #217 / PLC-19: nothing measurable, so there is genuinely no result to show. A 409
                // rather than a 400 because the request was valid and the sitting really did finish.
                return Problem(StatusCodes.Status409Conflict, "Placement test abandoned", ApiError.Codes.PlacementAbandoned, ex.Message);
            }
        });
    }
}
