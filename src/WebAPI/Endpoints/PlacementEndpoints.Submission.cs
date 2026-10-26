using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementSubmissionEndpoints(RouteGroupBuilder learner)
    {
        learner.MapPost("/placement/take/{placementId:int}/submit", async Task<IResult> (
            int placementId,
            [FromBody] SubmitPlacementTestRequest? request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                bool isAutoSubmit = request?.IsAutoSubmit ?? false;
                var result = await placementService.SubmitPlacementTestAsync(CurrentUserId(http), placementId, isAutoSubmit, cancellationToken);
                return Results.Ok(ToPlacementResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
        });
    }
}
