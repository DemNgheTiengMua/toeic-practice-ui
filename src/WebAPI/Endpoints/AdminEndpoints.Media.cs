using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapMedia(IEndpointRouteBuilder app)
    {
        static async Task<IResult> HandleExamMedia(
            int examId,
            string fileName,
            IFileStore fileStore,
            CancellationToken cancellationToken)
        {
            // The imports route beside this one has always checked the name; this one did not, so the
            // exams path accepted anything the route could carry. The store's own traversal guard is
            // the real defence, but a route that names the shape it serves costs one line and stops a
            // hostile name before it reaches the filesystem.
            if (!IsGeneratedName(fileName))
            {
                return Results.NotFound();
            }

            var info = await fileStore.GetMediaAsync($"exams/{examId}/{fileName}", cancellationToken);
            return info is null
                ? Results.NotFound()
                : Results.Stream(info.Stream, info.ContentType, enableRangeProcessing: true);
        }

        app.MapGet("/api/media/exams/{examId:int}/{fileName}", HandleExamMedia).RequireAuthorization();
        app.MapGet("/media/exams/{examId:int}/{fileName}", HandleExamMedia).RequireAuthorization();
    }
}
