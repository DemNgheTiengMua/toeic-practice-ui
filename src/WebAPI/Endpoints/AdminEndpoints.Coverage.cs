using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #202 / S-01: the coverage matrix. It reads the pool and writes nothing, but it sits on
    /// <c>pool.content.author</c> with the rest of the authoring surface: the screen exists to tell an
    /// author which cell to fill, which is an authoring act's question and not a blueprint or
    /// triage one.
    /// </summary>
    private static void MapCoverage(RouteGroupBuilder admin)
    {
        admin.MapGet("/coverage", async (IAdminCoverageService coverage, CancellationToken cancellationToken) =>
        {
            var matrix = await coverage.GetMatrixAsync(cancellationToken);
            return Results.Ok(matrix);
        });
    }
}
