using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AuthEndpoints
{
    private static void MapProfile(IEndpointRouteBuilder app)
    {

        // ---- Profile (issue #6) ----

        app.MapGet("/api/profile", async (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
        {
            var profile = await auth.GetProfileAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
        }).RequireAuthorization();

        app.MapPut("/api/profile", async Task<IResult> (
            UpdateProfileRequest request,
            HttpContext http,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var profile = await auth.UpdateProfileAsync(
                    CurrentUserId(http), new UpdateProfileInput(request.FullName, request.Phone, request.TimeZoneId), cancellationToken);
                return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        }).RequireAuthorization();

        app.MapPost("/api/profile/password", async Task<IResult> (
            ChangePasswordRequest request,
            HttpContext http,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await auth.ChangePasswordAsync(
                    CurrentUserId(http), new ChangePasswordInput(request.CurrentPassword, request.NewPassword), cancellationToken);
                return Results.NoContent();
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (AuthException ex) when (ex.Code == AuthException.InvalidCredentials)
            {
                return Problem(StatusCodes.Status400BadRequest, "Password change failed", ApiError.Codes.InvalidCredentials, ex.Message);
            }
            catch (AuthException ex) when (ex.Code == AuthException.Locked)
            {
                return Problem(StatusCodes.Status401Unauthorized, "Account locked", ApiError.Codes.Locked, ex.Message);
            }
        }).RequireRateLimiting("auth").RequireAuthorization();
    }
}
