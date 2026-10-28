using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static void MapPlacementSittingEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/placement/take/{placementId:int}/preflight", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await placementService.GetPlacementPreflightStatusAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(new PreflightStatusDto(status.AttemptId, status.ExamId, status.ExamName,
                    status.CertCode, status.CertName, status.DurationMinutes, status.RequiresPreflight,
                    status.RequiresAudio, status.RequiresMicrophone, status.IsVerified, status.VerifiedAt, status.AudioUrls));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
        });

        learner.MapGet("/placement/take/{placementId:int}", async Task<IResult> (
            int placementId,
            [FromQuery] int? q,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await placementService.GetPlacementTakeAsync(CurrentUserId(http), placementId, q, cancellationToken);
                return Results.Ok(ToPlacementTakeViewDto(take));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == ApiError.Codes.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
        });

        learner.MapPost("/placement/take/{placementId:int}/answers/{questionId:int}", async Task<IResult> (
            int placementId,
            int questionId,
            [FromBody] SaveAnswerRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SaveAnswerAsync(
                    CurrentUserId(http),
                    placementId,
                    questionId,
                    request.SelectedOptionId,
                    request.IsFlagged,
                    cancellationToken,
                    request.ResponseText,
                    request.SelectedOptionIdsJson);

                return Results.Ok(ToSaveAnswerResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Placement test expired", ApiError.Codes.PlacementExpired, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.Validation, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Question not available yet", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/placement/take/{placementId:int}/draft", async Task<IResult> (
            int placementId,
            [FromBody] SavePlacementDraftRequestDto request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SaveDraftAsync(
                    CurrentUserId(http),
                    placementId,
                    request.QuestionId,
                    request.ResponseText,
                    cancellationToken);

                return Results.Ok(ToSavePlacementDraftResponseDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Placement test expired", ApiError.Codes.PlacementExpired, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Question not available yet", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });
    }
}
