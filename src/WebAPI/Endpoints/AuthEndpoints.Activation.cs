using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AuthEndpoints
{
    private static void MapActivation(IEndpointRouteBuilder app)
    {

        // ---- Email verification and password reset (issue #5) ----
        // Links in mail point at the MVC site; it posts the token here. Tokens are single-use and expire.

        app.MapPost("/api/auth/verify-email", async Task<IResult> (
            VerifyEmailRequest request, IAuthService auth, CancellationToken cancellationToken) =>
        {
            try
            {
                await auth.VerifyEmailAsync(request.Token, cancellationToken);
            }
            catch (AuthException ex) when (ex.Code == AuthException.TokenInvalid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Link not valid", ApiError.Codes.TokenInvalid, ex.Message);
            }

            return Results.NoContent();
        });

        app.MapPost("/api/auth/resend-activation", async Task<IResult> (
            HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
        {
            var userId = CurrentUserId(http);
            try
            {
                await auth.ResendActivationAsync(userId, cancellationToken);
            }
            catch (EmailDeliveryException)
            {
                // Safe to surface here: the caller is signed in and owns the account.
                return Problem(StatusCodes.Status503ServiceUnavailable, "Could not send the email", ApiError.Codes.MailUnavailable);
            }

            return Results.NoContent();
        }).RequireAuthorization();
    }
}
