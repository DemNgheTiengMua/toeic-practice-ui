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
        app.MapGet("/api/media/imports/{batchId:int}/{fileName}", async Task<IResult> (
            int batchId,
            string fileName,
            IFileStore fileStore,
            CancellationToken cancellationToken) =>
        {
            if (!IsGeneratedName(fileName))
            {
                return Results.NotFound();
            }

            var info = await fileStore.GetMediaAsync($"imports/{batchId}/{fileName}", cancellationToken);
            return info is null
                ? Results.NotFound()
                : Results.Stream(info.Stream, info.ContentType, enableRangeProcessing: true);
        }).RequireAuthorization();
    }
}
