using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static void MapOverview(RouteGroupBuilder learner)
    {

        // GET /api/learner/learning-path: active path overview with modules and progress
        learner.MapGet("", async (
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            var view = await service.GetActivePathAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToSummaryDto(view));
        });
    }
}
