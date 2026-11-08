using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>
/// #216 / PLC-14, PLC-15, PLC-17: resume, explicit abandon and the recording-retry rule.
/// </summary>
public static partial class PlacementEndpoints
{
    private const string RecordingOutcomeRetry = "retry";
    private const string RecordingOutcomeFinalise = "finalise";
    private const string RecordingOutcomeReplace = "replace_with_sibling";

    private static void MapPlacementResumeEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/placement/take/{placementId:int}/resume", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await placementService.GetResumeAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(new PlacementResumeViewDto(
                    view.PlacementId,
                    view.AttemptId,
                    view.ExamName,
                    view.CertCode,
                    view.StartedAt,
                    view.ExpiresAt,
                    view.Skills.Select(s => new PlacementResumeSkillDto(
                        s.SkillId, s.Code, s.Name, s.IsDone, s.IsActive, s.AnsweredCount, s.TotalQuestions)).ToList(),
                    view.ActiveSkillId,
                    view.ActiveSkillName,
                    view.ActiveStage,
                    view.TotalStages,
                    view.SyncedAnswerCount,
                    view.TotalQuestions,
                    view.LastQuestionId));
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

        learner.MapPost("/placement/take/{placementId:int}/abandon", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await placementService.AbandonAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.NoContent();
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

        learner.MapPost("/placement/take/{placementId:int}/recording-interruptions", async Task<IResult> (
            int placementId,
            RecordingInterruptionRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var decision = await placementService.RecordRecordingInterruptionAsync(
                    CurrentUserId(http),
                    placementId,
                    request.QuestionId,
                    request.SessionToken,
                    request.CapturedMs,
                    cancellationToken);

                return Results.Ok(new RecordingDecisionDto(
                    decision.Interruptions,
                    decision.Outcome switch
                    {
                        BusinessObjects.PlacementRecordingOutcome.Retry => RecordingOutcomeRetry,
                        BusinessObjects.PlacementRecordingOutcome.Finalise => RecordingOutcomeFinalise,
                        _ => RecordingOutcomeReplace,
                    },
                    decision.ReplacementQuestionId));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });
    }
}
