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
        var admin = app.MapGroup("/api/admin/imports").RequireAuthorization(Roles.Admin);
        MapBatches(admin);
        MapUpload(admin);
        MapMedia(app);
    }

    /// <summary>32 hex characters and an allow-listed extension, as LocalFileStore.SaveImportMediaAsync names them.</summary>
    private static bool IsGeneratedName(string fileName)
    {
        var dot = fileName.IndexOf('.');
        return dot == 32
            && fileName[..dot].All(Uri.IsHexDigit)
            && fileName[dot..] is ".mp3" or ".wav" or ".jpg" or ".jpeg" or ".png";
    }
}
