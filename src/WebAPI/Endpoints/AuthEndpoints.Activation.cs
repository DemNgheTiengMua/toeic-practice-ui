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

        // ADR-0018: unauthenticated on purpose. An unverified account cannot sign in, so requiring a
        // token here would make a lost activation mail unrecoverable without an admin. Always 204:
        // the answer must not reveal whether the address has an account, exactly like forgot-password.
        app.MapPost("/api/auth/resend-activation", async Task<IResult> (
            ResendActivationRequest request, IAuthService auth, CancellationToken cancellationToken) =>
        {
            try
            {
                await auth.ResendActivationAsync(request.Email, cancellationToken);
            }
            catch (ValidationException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
            }

            return Results.NoContent();
        }).RequireRateLimiting("mail");
    }
}
