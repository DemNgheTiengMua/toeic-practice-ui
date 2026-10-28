using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapUsers(RouteGroupBuilder admin)
    {

        admin.MapGet("/users", async (
            string? q,
            string? status,
            int? page,
            int? pageSize,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            var summary = await users.GetUsersAsync(q, status, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapPost("/users/{userId:int}/lock", async (
            int userId,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var updated = await users.SetLockAsync(adminId, userId, isLocked: true, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/unlock", async (
            int userId,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var updated = await users.SetLockAsync(adminId, userId, isLocked: false, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/resend-activation", async (
            int userId,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await users.ResendActivationAsync(userId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/adjust-credits", async (
            int userId,
            AdjustCreditsRequest request,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var result = await users.AdjustCreditsAsync(adminId, userId, request.Delta, request.Reason, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });
    }
}
