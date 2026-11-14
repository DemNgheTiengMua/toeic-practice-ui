using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    /// <summary>
    /// #187: the request is one shape or the other. Building the input here keeps the refusal in the
    /// endpoint, where it can be a 400 with a reason, rather than in the service, where it would be an
    /// exception on a request that never had a chance.
    /// <para>
    /// #190: the tier is checked here too, for the same reason. The service validates it as well,
    /// because <see cref="StartPracticeSessionInput"/> must be safe however it is built; a bad tier
    /// reaching this endpoint is a caller's mistake, so it is answered before a query runs.
    /// </para>
    /// </summary>
    private static StartPracticeSessionInput? BuildStartInput(StartPracticeRequest request)
    {
        var hasCell = request.TopicId is not null && request.CefrLevelCode is not null;
        var hasSection = request.SectionId is not null;

        if (hasSection == hasCell)
        {
            return null;
        }

        if (hasSection)
        {
            return new StartPracticeSessionInput(request.SectionId!.Value);
        }

        if (request.ContentTier is not null && !ContentTiers.IsKnown(request.ContentTier))
        {
            return null;
        }

        return CefrLevel.TryParseAuthored(request.CefrLevelCode, out var level)
            ? new StartPracticeSessionInput(request.TopicId!.Value, level)
            {
                ContentTier = request.ContentTier ?? ContentTiers.Production,
            }
            : null;
    }

    private static void MapSessions(RouteGroupBuilder practice)
    {

        practice.MapPost("/sessions", async Task<IResult> (
            StartPracticeRequest request,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var input = BuildStartInput(request);
                if (input is null)
                {
                    // #190: a bad tier is not a bad shape, and saying "either a section or a cell"
                    // would send the caller looking at the wrong field.
                    return request.ContentTier is not null && !ContentTiers.IsKnown(request.ContentTier)
                        ? ValidationProblem(new ValidationException(
                            $"'{request.ContentTier}' is not a content tier. An item is either {ContentTiers.Production} or {ContentTiers.Demo}."))
                        : ValidationProblem(new ValidationException(
                            "A drill is started against either a section or a cell (topic and level), not both and not neither."));
                }

                var session = await practiceService.StartSessionAsync(CurrentUserId(http), input, cancellationToken);
                return Results.Ok(new PracticeSessionDto(
                    session.SessionId,
                    session.SectionId,
                    session.SectionCode,
                    session.SectionName,
                    session.Modality.MapTo<ModalityDto>(),
                    session.QuestionCount,
                    session.Status.MapTo<PracticeStatusDto>(),
                    session.CorrectCount,
                    session.QuestionSource.MapTo<QuestionSourceDto>(),
                    session.StartedAt,
                    session.FinishedAt,
                    session.TopicId,
                    session.CefrLevel?.Code));
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.FreeDrillLocked)
            {
                return Problem(StatusCodes.Status403Forbidden, "Practice section locked", ApiError.Codes.PracticeFreeDrillLocked, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.ModalityNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Modality not allowed for the free drill", ApiError.Codes.PracticeModalityNotAllowed, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SectionUnavailable)
            {
                return Problem(StatusCodes.Status404NotFound, "Section unavailable", ApiError.Codes.PracticeSectionUnavailable, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.NoQuestionsAvailable)
            {
                return Problem(StatusCodes.Status404NotFound, "No questions available", ApiError.Codes.PracticeNoQuestionsAvailable, ex.Message);
            }
        });

        practice.MapGet("/sessions/{sessionId:int}", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await practiceService.GetSessionInfoAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(new PracticeSessionDto(
                    session.SessionId,
                    session.SectionId,
                    session.SectionCode,
                    session.SectionName,
                    session.Modality.MapTo<ModalityDto>(),
                    session.QuestionCount,
                    session.Status.MapTo<PracticeStatusDto>(),
                    session.CorrectCount,
                    session.QuestionSource.MapTo<QuestionSourceDto>(),
                    session.StartedAt,
                    session.FinishedAt,
                    session.TopicId,
                    session.CefrLevel?.Code));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });
    }
}
