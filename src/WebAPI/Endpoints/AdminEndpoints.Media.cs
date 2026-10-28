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

        app.MapGet("/api/media/exams/{examId:int}/{fileName}", async Task<IResult> (
            int examId,
            string fileName,
            IFileStore fileStore,
            CancellationToken cancellationToken) =>
        {
            var info = await fileStore.GetMediaAsync($"exams/{examId}/{fileName}", cancellationToken);
            return info is null
                ? Results.NotFound()
                : Results.Stream(info.Stream, info.ContentType, enableRangeProcessing: true);
        }).RequireAuthorization();
    }
}
