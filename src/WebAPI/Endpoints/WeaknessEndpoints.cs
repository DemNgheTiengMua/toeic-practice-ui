using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WeaknessEndpoints
{
    public static void MapWeaknessEndpoints(this IEndpointRouteBuilder app)
    {
        var weakness = app.MapGroup("/api/learner")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Learner));

        weakness.MapGet("/weakness-analysis", async (
            HttpContext http,
            IWeaknessAnalysisService weaknessService,
            CancellationToken cancellationToken) =>
        {
            var view = await weaknessService.GetWeaknessAnalysisAsync(CurrentUserId(http), cancellationToken);
            var dto = ToDto(view);
            return Results.Ok(dto);
        });

        weakness.MapPost("/recommendations/{recId:int}/resolve", async (
            int recId,
            HttpContext http,
            IWeaknessAnalysisService weaknessService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await weaknessService.ResolveRecommendationAsync(CurrentUserId(http), recId, cancellationToken);
                return Results.Ok(new ResolveRecommendationResultDto(recId, true));
            }
            catch (RecommendationNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Recommendation not found", ApiError.Codes.RecommendationNotFound, ex.Message);
            }
        });
    }
}
