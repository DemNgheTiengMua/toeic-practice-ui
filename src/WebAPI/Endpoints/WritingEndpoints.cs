using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    public static void MapWritingEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/writing").RequireAuthorization(Roles.Learner);

        MapInstructionsRoutes(learner);
        MapStartRoutes(learner);
        MapTakeRoutes(learner);
        MapDraftRoutes(learner);
        MapSubmitRoutes(learner);
        MapPendingRoutes(learner);
        MapRetryRoutes(learner);
        MapReviewRoutes(learner);

    }
}
