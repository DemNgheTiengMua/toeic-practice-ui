using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>Question batch import (ADR-0007) and the endpoint that serves imported media.</summary>
public static partial class AdminImportEndpoints
{
    public static void MapAdminImportEndpoints(this IEndpointRouteBuilder app)
    {
        // D16: importing is authoring, so it is gated by pool.content.author.
        var admin = app.MapGroup("/api/admin/imports").RequireAuthorization(Permissions.ContentAuthor);
        MapBatches(admin);
        MapUpload(admin);
        MapMedia(app);
    }

    /// <summary>Imported media is named by the importer: 32 hex characters and an allow-listed extension.
    /// The check itself lives in <see cref="EndpointHelpers.IsGeneratedName"/> so the exams route can use
    /// the same rule rather than growing a second, weaker copy.</summary>
    private static bool IsGeneratedName(string fileName) => EndpointHelpers.IsGeneratedName(fileName);
}
