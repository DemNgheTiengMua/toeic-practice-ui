using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ComplaintEndpoints
{
    public static void MapComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Learner Complaints endpoints ----
        var learner = app.MapGroup("/api/learner/complaints").RequireAuthorization(Roles.Learner);

        // ---- Admin Complaints endpoints ----
        var admin = app.MapGroup("/api/admin/complaints").RequireAuthorization(Roles.Admin);
        MapLearner(learner);
        MapAdmin(admin);
    }
}
