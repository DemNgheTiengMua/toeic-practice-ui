using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapQuestions(RouteGroupBuilder practice)
    {

        practice.MapGet("/sessions/{sessionId:int}/take", async Task<IResult> (
            int sessionId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? q,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await practiceService.GetSessionTakeAsync(CurrentUserId(http), sessionId, q, cancellationToken);
                static PracticeOptionDto ToOptionDto(PracticeQuestionOption o) => new(o.OptionId, o.Label, o.Text);
                static PracticeQuestionDto ToQuestionDto(PracticeQuestionView qv) => new(
                    qv.SessionId, qv.SectionName, qv.QuestionId, qv.DisplayOrder, qv.TotalQuestions,
                    qv.Stem, qv.CategoryName, qv.Hint, qv.HintUsed, qv.Options.Select(ToOptionDto).ToList(),
                    qv.SelectedOptionId, qv.IsCorrect, qv.CorrectOptionId, qv.Explanation, qv.Transcript,
                    qv.Passage, qv.AudioPath, qv.ImagePath, qv.ImageAlt, qv.IsSaved, qv.AudioStartMs, qv.AudioEndMs,
                    qv.QuestionType, qv.MaxSelections, qv.AllowNumber, qv.AcceptedAnswersJson, qv.RequiredKeywords,
                    qv.MinWords, qv.MaxWords, qv.ResponseText, qv.SelectedOptionIds, qv.SelectedOptionIdsJson, qv.CorrectOptionIds);

                return Results.Ok(new PracticeSessionTakeDto(
                    new PracticeSessionDto(
                        take.Session.SessionId, take.Session.SectionId, take.Session.SectionCode, take.Session.SectionName,
                        take.Session.Modality.MapTo<ModalityDto>(), take.Session.QuestionCount, take.Session.Status.MapTo<PracticeStatusDto>(),
                        take.Session.CorrectCount, take.Session.QuestionSource.MapTo<QuestionSourceDto>(), take.Session.FeedbackMode.MapTo<FeedbackModeDto>(),
                        take.Session.StartedAt, take.Session.FinishedAt),
                    ToQuestionDto(take.Question),
                    take.Questions.Select(ToQuestionDto).ToList(),
                    take.Navigator.Select(n => new PracticeNavigatorItemDto(n.QuestionId, n.DisplayOrder, n.IsAnswered, n.IsCorrect, n.IsSaved, n.IsCurrent)).ToList()));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });

        practice.MapGet("/sessions/{sessionId:int}/questions", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var questions = await practiceService.GetSessionQuestionsAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(questions.Select(q => new PracticeQuestionDto(
                    q.SessionId,
                    q.SectionName,
                    q.QuestionId,
                    q.DisplayOrder,
                    q.TotalQuestions,
                    q.Stem,
                    q.CategoryName,
                    q.Hint,
                    q.HintUsed,
                    q.Options.Select(o => new PracticeOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
                    q.SelectedOptionId,
                    q.IsCorrect,
                    q.CorrectOptionId,
                    q.Explanation,
                    q.Transcript,
                    q.Passage,
                    q.AudioPath,
                    q.ImagePath,
                    q.ImageAlt,
                    q.IsSaved,
                    q.AudioStartMs,
                    q.AudioEndMs,
                    q.QuestionType,
                    q.MaxSelections,
                    q.AllowNumber,
                    q.AcceptedAnswersJson,
                    q.RequiredKeywords,
                    q.MinWords,
                    q.MaxWords,
                    q.ResponseText,
                    q.SelectedOptionIds,
                    q.SelectedOptionIdsJson,
                    q.CorrectOptionIds)).ToList());
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });

        practice.MapGet("/sessions/{sessionId:int}/questions/{displayOrder:int}", async Task<IResult> (
            int sessionId,
            int displayOrder,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var q = await practiceService.GetQuestionAsync(CurrentUserId(http), sessionId, displayOrder, cancellationToken);
                return Results.Ok(new PracticeQuestionDto(
                    q.SessionId,
                    q.SectionName,
                    q.QuestionId,
                    q.DisplayOrder,
                    q.TotalQuestions,
                    q.Stem,
                    q.CategoryName,
                    q.Hint,
                    q.HintUsed,
                    q.Options.Select(o => new PracticeOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
                    q.SelectedOptionId,
                    q.IsCorrect,
                    q.CorrectOptionId,
                    q.Explanation,
                    q.Transcript,
                    q.Passage,
                    q.AudioPath,
                    q.ImagePath,
                    q.ImageAlt,
                    q.IsSaved,
                    q.AudioStartMs,
                    q.AudioEndMs,
                    q.QuestionType,
                    q.MaxSelections,
                    q.AllowNumber,
                    q.AcceptedAnswersJson,
                    q.RequiredKeywords,
                    q.MinWords,
                    q.MaxWords,
                    q.ResponseText,
                    q.SelectedOptionIds,
                    q.SelectedOptionIdsJson,
                    q.CorrectOptionIds));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
            }
        });
    }
}
