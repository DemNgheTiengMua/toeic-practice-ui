using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ComplaintEndpoints
{
    private static void MapAdmin(RouteGroupBuilder admin)
    {

        admin.MapGet("", async (
            string? q,
            string? status,
            int? page,
            int? pageSize,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            var summary = await complaints.GetAdminComplaintsAsync(q, status, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapGet("/{complaintId:int}", async (
            int complaintId,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            var detail = await complaints.GetAdminComplaintDetailAsync(complaintId, cancellationToken);
            return detail is not null ? Results.Ok(detail) : Results.NotFound();
        });

        admin.MapPost("/{complaintId:int}/status", async (
            int complaintId,
            UpdateComplaintStatusRequest request,
            HttpContext http,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var domainStatus = request.Status.MapTo<ComplaintStatus>();
                var adminId = CurrentUserId(http);
                var updated = await complaints.SetComplaintStatusAsync(adminId, complaintId, domainStatus, cancellationToken);
                return Results.Ok(updated);
            }
            catch (ComplaintNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ComplaintException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/{complaintId:int}/resolve", async (
            int complaintId,
            ResolveComplaintRequest request,
            HttpContext http,
            IComplaintService complaints,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = CurrentUserId(http);
                var resolved = await complaints.ResolveComplaintAsync(adminId, complaintId, request, cancellationToken);
                return Results.Ok(resolved);
            }
            catch (ComplaintNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ComplaintException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
