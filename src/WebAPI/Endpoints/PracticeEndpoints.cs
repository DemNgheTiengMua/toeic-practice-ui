using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class PracticeEndpoints
{
    public static void MapPracticeEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // ---- Practice (issue #8) ----
        var practice = learner.MapGroup("/practice");

        practice.MapGet("/catalog", async (HttpContext http, IPracticeService practiceService, CancellationToken cancellationToken) =>
        {
            var view = await practiceService.GetPracticeSectionsAsync(CurrentUserId(http), cancellationToken);
            static PracticeSectionDto ToSectionDto(PracticeSectionItem s) => new(
                s.SectionId, s.SkillCode, s.SkillName, s.Modality.MapTo<ModalityDto>(), s.Code, s.Name, s.OptionCount,
                s.ExpectedQuestionCount, s.AvailableQuestionCount, s.AccuracyPercentage, s.AnsweredCount, s.DisplayOrder);

            return Results.Ok(new PracticeCatalogDto(
                ToCertificateDto(view.Certificate),
                view.ListeningSections.Select(ToSectionDto).ToList(),
                view.ReadingSections.Select(ToSectionDto).ToList(),
                view.OtherSections.Select(ToSectionDto).ToList(),
                view.WeakestSection is { } w ? ToSectionDto(w) : null,
                view.UnfinishedSession is { } ufs
                    ? new PracticeSessionDto(
                        ufs.SessionId, ufs.SectionId, ufs.SectionCode, ufs.SectionName, ufs.Modality.MapTo<ModalityDto>(),
                        ufs.QuestionCount, ufs.Status.MapTo<PracticeStatusDto>(), ufs.CorrectCount, ufs.QuestionSource.MapTo<QuestionSourceDto>(), ufs.FeedbackMode.MapTo<FeedbackModeDto>(),
                        ufs.StartedAt, ufs.FinishedAt)
                    : null));
        });

        practice.MapPost("/sessions", async Task<IResult> (
            StartPracticeRequest request,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var input = StartPracticeSessionInput.FromCodes(
                    request.SectionId,
                    request.QuestionCount,
                    request.QuestionSource,
                    request.FeedbackMode);

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
                    session.FeedbackMode.MapTo<FeedbackModeDto>(),
                    session.StartedAt,
                    session.FinishedAt));
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.ModalityNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Modality not allowed for free practice", ApiError.Codes.PracticeModalityNotAllowed, ex.Message);
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
                    session.FeedbackMode.MapTo<FeedbackModeDto>(),
                    session.StartedAt,
                    session.FinishedAt));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });

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

        practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/hint", async Task<IResult> (
            int sessionId,
            int questionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var hint = await practiceService.RevealHintAsync(CurrentUserId(http), sessionId, questionId, cancellationToken);
                return Results.Ok(new RevealHintResponse(hint, true));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
            }
        });

        practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/answer", async Task<IResult> (
            int sessionId,
            int questionId,
            SubmitPracticeAnswerRequest request,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await practiceService.SubmitAnswerAsync(
                    CurrentUserId(http),
                    sessionId,
                    questionId,
                    new SubmitPracticeAnswerInput(request.SelectedOptionId, request.ResponseText, request.SelectedOptionIdsJson, request.SelectedOptionIds),
                    cancellationToken);
                return Results.Ok(new GradedAnswerDto(
                    result.IsCorrect,
                    result.SelectedOptionId,
                    result.CorrectOptionId,
                    result.Explanation,
                    result.Transcript,
                    result.IsSessionFinished,
                    result.NextQuestionDisplayOrder,
                    result.IsSaved,
                    result.AcceptedAnswersJson,
                    result.CorrectOptionIds,
                    result.ResponseText,
                    result.SelectedOptionIdsJson));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
            }
        });

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
