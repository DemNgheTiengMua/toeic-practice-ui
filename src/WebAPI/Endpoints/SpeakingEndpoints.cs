using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class SpeakingEndpoints
{
    public static void MapSpeakingEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/speaking").RequireAuthorization(Roles.Learner);

        // GET /api/learner/speaking/{examId}/instructions
        learner.MapGet("/{examId:int}/instructions", async Task<IResult> (
            int examId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await speakingService.GetInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new SpeakingInstructionsDto(
                    view.ExamId,
                    view.CertId,
                    view.CertCode,
                    view.ExamName,
                    view.Description,
                    view.DurationMinutes,
                    view.QuestionCount,
                    view.CreditCost,
                    view.CreditBalance,
                    view.ActiveAttemptId,
                    view.Sections.Select(s => new SpeakingSectionInfoDto(
                        s.SectionId,
                        s.Code,
                        s.Name,
                        s.ExpectedQuestionCount,
                        s.PrepSeconds,
                        s.ResponseSeconds,
                        s.DisplayOrder)).ToList()));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        // POST /api/learner/speaking/{examId}/start
        learner.MapPost("/{examId:int}/start", async Task<IResult> (
            int examId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await speakingService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new StartSpeakingAttemptResponse(
                    attempt.AttemptId,
                    attempt.ExamId,
                    attempt.Status.MapTo<AttemptStatusDto>(),
                    attempt.StartedAt,
                    attempt.ExpiresAt,
                    attempt.RemainingSeconds,
                    attempt.IsAutoSubmitted));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        // GET /api/learner/speaking/attempts/{attemptId}
        learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
            int attemptId,
            [FromQuery] int? questionId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await speakingService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, questionId, cancellationToken);
                var q = take.Question;
                return Results.Ok(new SpeakingTakeDto(
                    take.AttemptId,
                    take.ExamId,
                    take.ExamName,
                    take.CertId,
                    take.CertCode,
                    take.Status.MapTo<AttemptStatusDto>(),
                    take.StartedAt,
                    take.ExpiresAt,
                    take.RemainingSeconds,
                    take.CurrentQuestionId,
                    take.CurrentQuestionIndex,
                    take.TotalQuestions,
                    take.SavedCount,
                    new SpeakingQuestionDto(
                        q.QuestionId,
                        q.DisplayOrder,
                        q.Stem,
                        q.PrepSeconds,
                        q.ResponseSeconds,
                        q.Hint,
                        q.SectionCode,
                        q.SectionName,
                        q.GroupImagePath,
                        q.GroupImageAlt,
                        q.GroupAudioPath,
                        q.AudioPath,
                        q.DurationMs,
                        q.ResponseText,
                        q.DraftSavedAt,
                        q.Status.MapTo<FreeResponseStatusDto>()),
                    take.Questions.Select(nav => new SpeakingQuestionNavDto(
                        nav.QuestionId,
                        nav.DisplayOrder,
                        nav.SectionCode,
                        nav.SectionName,
                        nav.IsAnswered,
                        nav.IsCurrent)).ToList()));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });

        // POST /api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio
        learner.MapPost("/attempts/{attemptId:int}/questions/{questionId:int}/audio", async Task<IResult> (
            int attemptId,
            int questionId,
            HttpRequest request,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.", ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("audioFile");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Audio file is required.", ApiError.Codes.Validation, "Audio file cannot be empty.");
            }

            int? durationMs = null;
            if (form.TryGetValue("durationMs", out var durationValue) && int.TryParse(durationValue, out int parsedDuration))
            {
                durationMs = parsedDuration;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var result = await speakingService.SaveAudioAsync(
                    CurrentUserId(http),
                    attemptId,
                    questionId,
                    stream,
                    file.ContentType,
                    file.FileName,
                    durationMs,
                    cancellationToken);

                return Results.Ok(new UploadSpeakingAudioResponse(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.AudioPath,
                    result.DurationMs,
                    result.SavedAt));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.InvalidAudio)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid audio file", ApiError.Codes.Validation, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        }).DisableAntiforgery();

        // GET /api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio (ownership-checked audio endpoint)
        learner.MapGet("/attempts/{attemptId:int}/questions/{questionId:int}/audio", async Task<IResult> (
            int attemptId,
            int questionId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var fileInfo = await speakingService.GetAudioFileAsync(CurrentUserId(http), attemptId, questionId, cancellationToken);
                if (fileInfo is null)
                {
                    return Problem(StatusCodes.Status404NotFound, "Audio file not found", ApiError.Codes.AttemptNotFound, "The audio file could not be found.");
                }

                return Results.Stream(fileInfo.Stream, fileInfo.ContentType, fileInfo.FileName, enableRangeProcessing: true);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.Forbidden)
            {
                return Problem(StatusCodes.Status403Forbidden, "Forbidden", ApiError.Codes.Forbidden, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound || ex.Code == SpeakingPracticeException.AudioNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Audio not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });

        // POST /api/learner/speaking/attempts/{attemptId}/submit
        learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await speakingService.SubmitAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new SubmitSpeakingResponse(
                    result.AttemptId,
                    result.Status.MapTo<AttemptStatusDto>(),
                    result.SubmittedAt));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.Validation)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        // GET /api/learner/speaking/attempts/{attemptId}/pending
        learner.MapGet("/attempts/{attemptId:int}/pending", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await speakingService.GetGradingPendingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new SpeakingGradingPendingDto(
                    view.AttemptId,
                    view.ExamId,
                    view.ExamName,
                    view.CertCode,
                    view.AttemptStatus.MapTo<AttemptStatusDto>(),
                    view.TotalResponses,
                    view.GradedResponses,
                    view.FailedResponses,
                    view.PendingResponses,
                    view.CanRetry,
                    view.FailureReason,
                    view.Items.Select(item => new FreeResponseGradingStatusDto(
                        item.FreeResponseId,
                        item.QuestionId,
                        item.DisplayOrder,
                        item.Status.MapTo<AiGradingStatusDto>(),
                        item.AttemptCount,
                        item.FailureReason)).ToList()));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });

        // POST /api/learner/speaking/attempts/{attemptId}/retry
        learner.MapPost("/attempts/{attemptId:int}/retry", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await speakingService.RetryGradingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.RetryNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
        });

        // GET /api/learner/speaking/attempts/{attemptId}/review
        learner.MapGet("/attempts/{attemptId:int}/review", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var review = await speakingService.GetAttemptReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                SpeakingQuestionReviewItemDto? lowestDto = null;
                if (review.LowestScoredQuestion != null)
                {
                    var l = review.LowestScoredQuestion;
                    lowestDto = new SpeakingQuestionReviewItemDto(
                        l.QuestionId,
                        l.DisplayOrder,
                        l.SectionName,
                        l.Stem,
                        l.AudioPath,
                        l.DurationMs,
                        l.Transcript,
                        l.TotalScore,
                        l.MaxScore,
                        l.Criteria.Select(c => new SpeakingCriterionScoreItemDto(
                            c.CriterionId,
                            c.Code,
                            c.Name,
                            c.Score,
                            c.MaxScore,
                            c.Comment)).ToList());
                }

                return Results.Ok(new SpeakingReviewDto(
                    review.AttemptId,
                    review.ExamId,
                    review.ExamName,
                    review.CertCode,
                    review.SubmittedAt,
                    review.TotalQuestions,
                    review.AnsweredQuestions,
                    review.TotalScore,
                    review.MaxScore,
                    review.ScaledScore,
                    review.Questions.Select(q => new SpeakingQuestionReviewItemDto(
                        q.QuestionId,
                        q.DisplayOrder,
                        q.SectionName,
                        q.Stem,
                        q.AudioPath,
                        q.DurationMs,
                        q.Transcript,
                        q.TotalScore,
                        q.MaxScore,
                        q.Criteria.Select(c => new SpeakingCriterionScoreItemDto(
                            c.CriterionId,
                            c.Code,
                            c.Name,
                            c.Score,
                            c.MaxScore,
                            c.Comment)).ToList())).ToList(),
                    lowestDto));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not graded yet", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });
    }
}
