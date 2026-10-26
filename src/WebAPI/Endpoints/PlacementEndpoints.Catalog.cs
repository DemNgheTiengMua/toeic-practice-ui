using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementCatalogEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/placement", async (
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            var view = await placementService.GetPlacementCatalogAsync(CurrentUserId(http), cancellationToken);
            var dto = ToPlacementCatalogViewDto(view);

            return Results.Ok(dto);
        });

        learner.MapPost("/placement/start", async Task<IResult> (
            [FromBody] StartPlacementTestRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var info = await placementService.StartPlacementTestAsync(CurrentUserId(http), request.ExamId, cancellationToken);
                return Results.Ok(ToPlacementTestInfoDto(info));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });
    }
}
