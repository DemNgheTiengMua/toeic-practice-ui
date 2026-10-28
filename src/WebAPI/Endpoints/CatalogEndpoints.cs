using Contracts;
using Services;

namespace WebAPI.Endpoints;

public static partial class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        MapCertificates(app);
        MapPackages(app);
    }
}
