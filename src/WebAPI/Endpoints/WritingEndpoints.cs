using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class WritingEndpoints
{
    public static void MapWritingEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/writing").RequireAuthorization(Roles.Learner);

        // GET /api/learner/writing/{examId}/instructions
        learner.MapGet("/{examId:int}/instructions", async Task<IResult> (
            int examId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await writingService.GetInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new WritingInstructionsDto(
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
                    view.Sections.Select(s => new WritingSectionInfoDto(
                        s.SectionId,
                        s.Code,
                        s.Name,
                        s.ExpectedQuestionCount,
                        s.ResponseSeconds,
                        s.DisplayOrder)).ToList()));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        // POST /api/learner/writing/{examId}/start
        learner.MapPost("/{examId:int}/start", async Task<IResult> (
            int examId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await writingService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new StartWritingAttemptResponse(
                    attempt.AttemptId,
                    attempt.ExamId,
                    attempt.Status.MapTo<AttemptStatusDto>(),
                    attempt.StartedAt,
                    attempt.ExpiresAt,
                    attempt.RemainingSeconds,
                    attempt.IsAutoSubmitted));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        // GET /api/learner/writing/attempts/{attemptId}
        learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
            int attemptId,
            [FromQuery] int? questionId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await writingService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, questionId, cancellationToken);
                var q = take.Question;
                return Results.Ok(new WritingTakeDto(
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
                    new WritingQuestionDto(
                        q.QuestionId,
                        q.DisplayOrder,
                        q.Stem,
                        q.MinWords,
                        q.MaxWords,
                        q.ResponseSeconds,
                        q.Hint,
                        q.SectionCode,
                        q.SectionName,
                        q.GroupImagePath,
                        q.GroupImageAlt,
                        q.ResponseText,
                        q.DraftSavedAt,
                        q.Status.MapTo<FreeResponseStatusDto>()),
                    take.Questions.Select(nav => new WritingQuestionNavDto(
                        nav.QuestionId,
                        nav.DisplayOrder,
                        nav.SectionCode,
                        nav.SectionName,
                        nav.IsAnswered,
                        nav.IsCurrent)).ToList()));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });

        // POST /api/learner/writing/attempts/{attemptId}/draft
        learner.MapPost("/attempts/{attemptId:int}/draft", async Task<IResult> (
            int attemptId,
            [FromBody] SaveWritingDraftRequest request,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await writingService.SaveDraftAsync(CurrentUserId(http), attemptId, request.QuestionId, request.ResponseText, cancellationToken);
                return Results.Ok(new SaveWritingDraftResponse(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.SavedAt,
                    result.WordCount));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });

        // POST /api/learner/writing/attempts/{attemptId}/submit
        learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await writingService.SubmitAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new SubmitWritingResponse(
                    result.AttemptId,
                    result.Status.MapTo<AttemptStatusDto>(),
                    result.SubmittedAt));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        // GET /api/learner/writing/attempts/{attemptId}/pending
        learner.MapGet("/attempts/{attemptId:int}/pending", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await writingService.GetGradingPendingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new WritingGradingPendingDto(
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
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });

        // POST /api/learner/writing/attempts/{attemptId}/retry
        learner.MapPost("/attempts/{attemptId:int}/retry", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await writingService.RetryGradingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.RetryNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
        });

        // GET /api/learner/writing/attempts/{attemptId}/review
        learner.MapGet("/attempts/{attemptId:int}/review", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var review = await writingService.GetAttemptReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new WritingReviewDto(
                    review.AttemptId,
                    review.ExamId,
                    review.ExamName,
                    review.CertCode,
                    review.SubmittedAt,
                    review.TotalQuestions,
                    review.AnsweredQuestions,
                    review.TotalScore,
                    review.MaxScore,
                    review.Questions.Select(q => new WritingQuestionReviewItemDto(
                        q.QuestionId,
                        q.DisplayOrder,
                        q.SectionName,
                        q.Stem,
                        q.ResponseText,
                        q.WordCount,
                        q.TotalScore,
                        q.MaxScore,
                        q.Criteria.Select(c => new WritingCriterionScoreItemDto(
                            c.CriterionId,
                            c.Code,
                            c.Name,
                            c.Score,
                            c.MaxScore,
                            c.Comment)).ToList())).ToList()));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not graded yet", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });
    }
}
