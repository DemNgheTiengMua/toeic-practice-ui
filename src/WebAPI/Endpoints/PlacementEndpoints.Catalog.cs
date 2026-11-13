using BusinessObjects;
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

        // #213 / SCR-PL-01: what will be measured, what may be carried over, and the cooldown. Free: it
        // reads no balance and writes nothing.
        learner.MapGet("/placement/preflight", async (
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            var view = await placementService.GetPreflightAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToPlacementPreflightViewDto(view));
        });

        learner.MapPost("/placement/start", async Task<IResult> (
            [FromBody] StartPlacementTestRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var info = await placementService.StartPlacementTestAsync(
                    CurrentUserId(http),
                    request.ExamId,
                    request.ReuseValidSkills ? PlacementReuseChoice.ReuseValid : PlacementReuseChoice.Remeasure,
                    request.Scope.MapTo<PlacementScope>(),
                    request.IdempotencyKey,
                    cancellationToken);
                return Results.Ok(ToPlacementTestInfoDto(info));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementCooldown)
            {
                return Problem(StatusCodes.Status429TooManyRequests, "Retake cooldown", ApiError.Codes.PlacementCooldown, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
        });
    }
}
