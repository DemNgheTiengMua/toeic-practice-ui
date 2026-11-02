using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static void MapLifecycle(RouteGroupBuilder learner)
    {

        // POST /api/learner/learning-path/reset: reset current path
        learner.MapPost("/reset", async (
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            var success = await service.ResetActivePathAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new ResetLearningPathResultDto(success));
        });
    }
}
