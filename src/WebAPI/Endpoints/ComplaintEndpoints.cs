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
        // D16: resolving a learner complaint is support triage. Read-only triage (ADM-03) is #201;
        // this ticket only moves the gate off the role name.
        var admin = app.MapGroup("/api/admin/complaints").RequireAuthorization(Permissions.SupportTriage);
        MapLearner(learner);
        MapAdmin(admin);
    }
}
