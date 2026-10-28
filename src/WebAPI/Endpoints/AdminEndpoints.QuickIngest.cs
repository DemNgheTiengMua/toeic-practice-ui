using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapQuickIngest(RouteGroupBuilder admin)
    {

        admin.MapPost("/exams/{examId:int}/quick-ingest/parse", async (
            int examId,
            QuickIngestParseRequest request,
            IExamQuickIngestService quickIngest,
            CancellationToken cancellationToken) =>
        {
            var result = await quickIngest.ParseStagingAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        admin.MapGet("/sections/{sectionId:int}/quick-ingest/sample", async (
            int sectionId,
            IExamQuickIngestService quickIngest,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var template = await quickIngest.GetSampleTemplateAsync(sectionId, cancellationToken);
                return Results.Ok(new QuickIngestSampleTemplateDto(template, "Section Blueprint Tailored Template"));
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/quick-ingest/commit", async (
            int examId,
            QuickIngestCommitRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await exams.CommitQuickIngestAsync(examId, request, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
