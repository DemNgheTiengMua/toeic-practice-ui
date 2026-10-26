using Contracts;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    public static void MapPlacementEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        MapPlacementCatalogEndpoints(learner);
        MapPlacementSittingEndpoints(learner);
        MapPlacementGradingEndpoints(learner);
        MapPlacementSubmissionEndpoints(learner);
        MapPlacementResultsEndpoints(learner);
        MapPlacementTargetBandEndpoints(learner);
    }
}
