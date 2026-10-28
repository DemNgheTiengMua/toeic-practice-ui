using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapPackages(RouteGroupBuilder admin)
    {

        admin.MapGet("/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
        {
            var summary = await packages.GetAdminPackagesAsync(cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapGet("/packages/{packageId:int}", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
        {
            var pkg = await packages.GetPackageByIdAsync(packageId, cancellationToken);
            return pkg is not null ? Results.Ok(pkg) : Results.NotFound();
        });

        admin.MapPost("/packages", async (CreatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await packages.CreatePackageAsync(request, cancellationToken);
                return Results.Created($"/api/admin/packages/{created.PackageId}", created);
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPut("/packages/{packageId:int}", async (int packageId, UpdatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await packages.UpdatePackageAsync(packageId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPost("/packages/{packageId:int}/deactivate", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                await packages.DeactivatePackageAsync(packageId, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }
}
