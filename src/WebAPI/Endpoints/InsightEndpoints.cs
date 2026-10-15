using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class InsightEndpoints
{
    public static void MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/insights").RequireAuthorization(Roles.Learner);

        // GET /api/learner/insights/practice/{sessionId}
        learner.MapGet("/practice/{sessionId:int}", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreatePracticeInsightAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.PracticeSessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Practice session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/practice/{sessionId}/retry
        learner.MapPost("/practice/{sessionId:int}/retry", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryPracticeInsightAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.NoContent();
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Insight not found", ApiError.Codes.InsightNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFailed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // GET /api/learner/insights/exam/{attemptId}
        learner.MapGet("/exam/{attemptId:int}", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreateExamInsightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/exam/{attemptId}/retry
        learner.MapPost("/exam/{attemptId:int}/retry", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryExamInsightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Insight not found", ApiError.Codes.InsightNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFailed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/{insightId}/retry
        learner.MapPost("/{insightId:int}/retry", async Task<IResult> (
            int insightId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryInsightAsync(CurrentUserId(http), insightId, cancellationToken);
                return Results.NoContent();
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Insight not found", ApiError.Codes.InsightNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFailed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // GET /api/learner/insights/dashboard
        learner.MapGet("/dashboard", async (
            int? certId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreateDashboardInsightAsync(CurrentUserId(http), certId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.CertificateUnavailable)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/dashboard/retry
        learner.MapPost("/dashboard/retry", async (
            int? certId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryDashboardInsightAsync(CurrentUserId(http), certId, cancellationToken);
                return Results.NoContent();
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Insight not found", ApiError.Codes.InsightNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFailed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });
    }

    private static AiInsightDto ToDto(AiInsightView view) => new(
        view.InsightId,
        view.UserId,
        view.CertId,
        view.Scope.MapTo<AiInsightScopeDto>(),
        view.PracticeSessionId,
        view.AttemptId,
        view.Narrative,
        view.Trend?.MapTo<AiInsightTrendDto>(),
        view.Status.MapTo<AiInsightStatusDto>(),
        view.FailureReason,
        view.Model,
        view.PromptTokens,
        view.CompletionTokens,
        view.SourceFingerprint,
        view.CreatedAt,
        view.CompletedAt,
        view.Recommendations.Select(r => new RecommendationDto(
            r.RecId,
            view.InsightId,
            view.PracticeSessionId,
            view.AttemptId,
            r.Text,
            r.Severity.MapTo<RecommendationSeverityDto>(),
            r.TargetBandCode,
            r.SkillName,
            r.IsResolved,
            r.CreatedAt)).ToList()
    );
}
