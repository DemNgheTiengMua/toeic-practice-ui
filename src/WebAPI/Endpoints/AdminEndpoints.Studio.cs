using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapStudio(RouteGroupBuilder admin)
    {

        admin.MapGet("/exams/{examId:int}/studio", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var studio = await exams.GetExamStudioAsync(examId, cancellationToken);
                return Results.Ok(studio);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/exams/{examId:int}/audit", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var audit = await exams.AuditExamPublishReadinessAsync(examId, cancellationToken);
                return Results.Ok(audit);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/reorder", async (
            int examId,
            ReorderSectionItemsRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.ReorderSectionItemsAsync(examId, request, cancellationToken);
                return Results.Ok(new { success = true });
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/clusters", async (
            int examId,
            CreateExamClusterRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var cluster = await exams.CreateExamClusterAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/exams/{examId}/groups/{cluster.GroupId}", cluster);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
