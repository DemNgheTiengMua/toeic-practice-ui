using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;
using BusinessObjects;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    /// <summary>
    /// #156: the calibration check and the learner's answer to it. Both live on the placement group
    /// because the band being questioned is the placement result.
    /// </summary>
    private static void MapCalibrationEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/calibration", async (
            HttpContext http,
            ICalibrationService service,
            CancellationToken cancellationToken) =>
        {
            var view = await service.GetCalibrationAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToCalibrationViewDto(view));
        });

        learner.MapPost("/calibration/decision", async Task<IResult> (
            [FromBody] CalibrationDecisionRequest request,
            HttpContext http,
            ICalibrationService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.DecideAsync(CurrentUserId(http), request.Adjust, cancellationToken);
                return Results.Ok(new CalibrationDecisionResultDto(
                    result.Decision, result.PlacedBandCode, result.ProposedBandCode, result.PathAdjusted));
            }
            catch (CalibrationException ex) when (ex.Code == CalibrationException.NoProposal)
            {
                // A stale tab is the likely cause, and it is not a server fault: the screen reloads
                // and shows the evidence as it stands now.
                return Problem(StatusCodes.Status409Conflict, "No calibration to answer", ex.Code, ex.Message);
            }
        });
    }

    private static CalibrationViewDto ToCalibrationViewDto(CalibrationView view) =>
        new(
            view.PlacedBandCode,
            view.PlacedBandName,
            view.ValidAnswerCount,
            view.RequiredValidAnswerCount,
            view.ExcludedRushedCount,
            view.ObservedAccuracyPct,
            view.Reason,
            view.Proposal is { } p
                ? new CalibrationProposalDto(
                    p.ProposedBandCode, p.ProposedBandName, p.ObservedAccuracyPct,
                    p.ValidAnswerCount, p.RequiredAccuracyPct, p.Evidence)
                : null);
}
