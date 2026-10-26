using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementGradingEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/placement/result/{placementId:int}/status", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await placementService.GetGradingStatusAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(ToPlacementGradingStatusDto(status));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
        });
    }
}
