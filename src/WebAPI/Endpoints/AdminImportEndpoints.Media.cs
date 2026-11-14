using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminImportEndpoints
{
    private static void MapMedia(IEndpointRouteBuilder app)
    {
        // GET /api/media/imports/{batchId}/{fileName}: imported audio and images for any signed-in
        // user (learners need them to sit the exam). The store resolves the path inside its own
        // root, and only names this importer generates are accepted.
        static async Task<IResult> HandleImportMedia(
            int batchId,
            string fileName,
            IFileStore fileStore,
            CancellationToken cancellationToken)
        {
            if (!IsGeneratedName(fileName))
            {
                return Results.NotFound();
            }

            var info = await fileStore.GetMediaAsync($"imports/{batchId}/{fileName}", cancellationToken);
            return info is null
                ? Results.NotFound()
                : Results.Stream(info.Stream, info.ContentType, enableRangeProcessing: true);
        }

        app.MapGet("/api/media/imports/{batchId:int}/{fileName}", HandleImportMedia).RequireAuthorization();
        app.MapGet("/media/imports/{batchId:int}/{fileName}", HandleImportMedia).RequireAuthorization();
    }
}
