using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapResults(RouteGroupBuilder practice)
    {

        practice.MapGet("/sessions/{sessionId:int}/summary", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var s = await practiceService.GetSummaryAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(new PracticeSummaryDto(
                    s.SessionId,
                    s.SectionCode,
                    s.SectionName,
                    s.Modality.MapTo<ModalityDto>(),
                    s.StartedAt,
                    s.FinishedAt,
                    s.TotalQuestions,
                    s.AnsweredCount,
                    s.CorrectCount,
                    s.IncorrectCount,
                    s.SkippedCount,
                    s.AccuracyPercentage,
                    s.HintUsedCount,
                    s.CorrectWithHintCount,
                    s.IncorrectWithHintCount,
                    s.Questions.Select(q => new QuestionOutcomeDto(
                        q.DisplayOrder,
                        q.QuestionId,
                        q.CategoryName,
                        q.Stem,
                        q.SelectedOptionLabel,
                        q.SelectedOptionText,
                        q.CorrectOptionLabel,
                        q.CorrectOptionText,
                        q.IsCorrect,
                        q.HintUsed,
                        q.Explanation,
                        q.AudioPath,
                        q.AudioStartMs,
                        q.AudioEndMs,
                        q.Transcript)).ToList()));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });
    }
}
