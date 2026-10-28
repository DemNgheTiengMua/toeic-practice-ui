using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapSummary(RouteGroupBuilder admin)
    {

        admin.MapGet("/summary", (HttpContext http) =>
            Results.Ok(new { admin = http.User.FindFirst(IAccessTokenFactory.ClaimEmail)!.Value }));
    }
}
