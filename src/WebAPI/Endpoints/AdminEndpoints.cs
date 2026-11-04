using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Admin endpoints ----
        // D16: no route here is gated on a role name. Each mapper gets its own group over the same
        // prefix, requiring one capability, so what an account may do is what its role was granted
        // and not what the role is called.
        //
        //   pool.content.author           exams, questions, quick-ingest, studio, imports, lessons
        //   assessment.blueprint.design   certificates, score conversion
        //   operations.support.triage     complaints, mentors
        //   system.governance.admin       summary, dashboard, packages, orders, users
        //
        // pool.content.review has no endpoint yet: the review queue and its two invariants are #201.
        MapSummary(Capability(app, Permissions.SystemAdmin));
        MapDashboard(Capability(app, Permissions.SystemAdmin));
        MapPackages(Capability(app, Permissions.SystemAdmin));
        MapOrders(Capability(app, Permissions.SystemAdmin));
        MapUsers(Capability(app, Permissions.SystemAdmin));
        MapScoreConversion(Capability(app, Permissions.BlueprintDesign));
        MapExams(Capability(app, Permissions.ContentAuthor));
        MapQuestions(Capability(app, Permissions.ContentAuthor));
        MapQuickIngest(Capability(app, Permissions.ContentAuthor));
        MapStudio(Capability(app, Permissions.ContentAuthor));
        MapCertificates(Capability(app, Permissions.BlueprintDesign));
        MapMedia(app);
    }

    /// <summary>One group per capability. Two <c>RequireAuthorization</c> calls on the same group
    /// would AND their policies and refuse everybody, which is why these are siblings rather than
    /// one group with several requirements.</summary>
    private static RouteGroupBuilder Capability(IEndpointRouteBuilder app, string capability) =>
        app.MapGroup("/api/admin").RequireAuthorization(capability);
}
