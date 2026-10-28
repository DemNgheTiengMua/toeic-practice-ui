using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ComplaintEndpoints
{
    private static void MapLearner(RouteGroupBuilder learner)
    {

        learner.MapGet("", async (
            HttpContext http,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            var list = await complaints.GetLearnerComplaintsAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(list);
        });

        learner.MapGet("/eligible-subjects", async (
            HttpContext http,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            var subjects = await complaints.GetEligibleSubjectsAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(subjects);
        });

        learner.MapPost("", async (
            FileComplaintRequest request,
            HttpContext http,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await complaints.FileComplaintAsync(CurrentUserId(http), request, cancellationToken);
                return Results.Created($"/api/learner/complaints/{created.ComplaintId}", created);
            }
            catch (ComplaintException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
