using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    public static void MapLearningPathEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/learning-path").RequireAuthorization(Roles.Learner);
        MapOverview(learner);
        MapGeneration(learner);
        MapModules(learner);
        MapLifecycle(learner);
    }
}
