using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    public static void MapPracticeEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // ---- Practice (issue #8) ----
        var practice = learner.MapGroup("/practice");
        MapCatalog(practice);
        MapSessions(practice);
        MapQuestions(practice);
        MapAnswers(practice);
        MapResults(practice);
    }
}
