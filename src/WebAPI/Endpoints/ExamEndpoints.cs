using Contracts;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    public static void MapExamEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        MapExamCatalogEndpoints(learner);
        MapExamSittingEndpoints(learner);
        MapExamPreflightEndpoints(learner);
        MapExamAnswersEndpoints(learner);
        MapExamSubmissionEndpoints(learner);
        MapExamTransitionsEndpoints(learner);
        MapExamPersistenceEndpoints(learner);
        MapExamResultsEndpoints(learner);
    }
}
