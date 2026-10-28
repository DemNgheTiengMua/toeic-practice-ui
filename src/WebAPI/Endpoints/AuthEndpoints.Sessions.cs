using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AuthEndpoints
{
    private static void MapSessions(IEndpointRouteBuilder app)
    {

        app.MapPost("/api/auth/sign-in", async Task<IResult> (
            SignInRequest request,
            IAuthService auth,
            IAccessTokenFactory tokens,
            CancellationToken cancellationToken) =>
        {
            AuthUser user;
            try
            {
                user = await auth.SignInAsync(new SignInInput(request.Email, request.Password), cancellationToken);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (AuthException ex) when (ex.Code == AuthException.InvalidCredentials)
            {
                return Problem(StatusCodes.Status401Unauthorized, "Sign-in failed", ApiError.Codes.InvalidCredentials, ex.Message);
            }
            catch (AuthException ex) when (ex.Code == AuthException.Locked)
            {
                return Problem(StatusCodes.Status401Unauthorized, "Account locked", ApiError.Codes.Locked, ex.Message);
            }

            return ToTokenResponse(user, tokens);
        }).RequireRateLimiting("auth");

        // Returns the live database row, not the token's claims: the MVC site's cookie revalidation
        // compares against this to catch server-side changes (role, name) since sign-in.
        app.MapGet("/api/auth/me", async Task<IResult> (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
        {
            var user = await auth.GetUserAsync(CurrentUserId(http), cancellationToken);
            if (user is null)
            {
                return Problem(StatusCodes.Status404NotFound, "Not Found", ApiError.Codes.NotFound);
            }

            return Results.Ok(ToDto(user));
        }).RequireAuthorization();
    }
}
