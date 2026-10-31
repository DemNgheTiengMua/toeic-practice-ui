using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementTargetBandEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/target-band", async (
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            var view = await placementService.GetTargetBandViewAsync(CurrentUserId(http), cancellationToken);
            var dto = ToTargetBandViewDto(view);

            return Results.Ok(dto);
        });

        learner.MapPost("/target-band", async Task<IResult> (
            [FromBody] SetTargetBandRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SetTargetBandAsync(CurrentUserId(http), request.TargetBandCode, cancellationToken);
                return Results.Ok(ToSetTargetBandResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.TargetBandInvalid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid target band", ApiError.Codes.TargetBandInvalid, ex.Message);
            }
        });

        // #153: the one-step onboarding form — band, exam date, weekly pace, and the verdict.
        learner.MapPost("/goal", async Task<IResult> (
            [FromBody] SetGoalRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SetGoalAsync(
                    CurrentUserId(http),
                    request.CurrentBandCode,
                    request.TargetBandCode,
                    request.TargetDate,
                    request.WeeklyStudyHours,
                    cancellationToken);

                return Results.Ok(ToSetGoalResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code is PlacementException.TargetBandInvalid
                or PlacementException.WeeklyHoursTooHigh
                or PlacementException.TargetDateInvalid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid goal", ex.Code, ex.Message);
            }
        });
    }
}
