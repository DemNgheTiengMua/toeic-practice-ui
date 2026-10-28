using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminImportEndpoints
{
    private static void MapBatches(RouteGroupBuilder admin)
    {

        admin.MapGet("", async (int? certId, IQuestionImportService imports, CancellationToken cancellationToken) =>
            Results.Ok(await imports.GetBatchesAsync(certId, cancellationToken)));

        admin.MapGet("/manifest-template", async (
            int certId,
            IQuestionImportService imports,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (stream, contentType, fileName) = await imports.GenerateManifestTemplateAsync(certId, cancellationToken);
                return Results.File(stream, contentType, fileName);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/{batchId:int}", async (int batchId, IQuestionImportService imports, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await imports.GetBatchAsync(batchId, cancellationToken));
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
