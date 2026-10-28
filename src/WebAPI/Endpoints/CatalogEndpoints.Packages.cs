using Contracts;
using Services;

namespace WebAPI.Endpoints;

public static partial class CatalogEndpoints
{
    private static void MapPackages(IEndpointRouteBuilder app)
    {

        app.MapGet("/api/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
        {
            var list = await packages.GetActivePackagesAsync(cancellationToken);
            return Results.Ok(list);
        });
    }
}
