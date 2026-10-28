using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    public static void MapSpeakingEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/speaking").RequireAuthorization(Roles.Learner);

        MapInstructionsRoutes(learner);
        MapStartRoutes(learner);
        MapTakeRoutes(learner);
        MapUploadRoutes(learner);
        MapAudioRoutes(learner);
        MapSubmitRoutes(learner);
        MapPendingRoutes(learner);
        MapRetryRoutes(learner);
        MapReviewRoutes(learner);

    }
}
