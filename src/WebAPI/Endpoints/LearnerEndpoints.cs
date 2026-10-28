using System.ComponentModel.DataAnnotations;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearnerEndpoints
{
    public static void MapLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Learner (issue #7) ----
        // The learner is always the token's own user: no endpoint takes a user id or a certificate to
        // filter by, so one learner can't read another's data and no screen can mix certificates.

        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);
        MapContext(learner);
        MapDashboard(learner);
    }
}
