using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AuthEndpoints
{
    private static void MapRecovery(IEndpointRouteBuilder app)
    {

        // Always 204 for a well-formed address, account or not, sent or not: the answer must not
        // enumerate users. The service logs a failed send instead of raising it.
        app.MapPost("/api/auth/forgot-password", async Task<IResult> (
            ForgotPasswordRequest request, IAuthService auth, CancellationToken cancellationToken) =>
        {
            try
            {
                await auth.RequestPasswordResetAsync(new ForgotPasswordInput(request.Email), cancellationToken);
            }
            catch (ValidationException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
            }

            return Results.NoContent();
        });

        app.MapPost("/api/auth/reset-password", async Task<IResult> (
            ResetPasswordRequest request, IAuthService auth, CancellationToken cancellationToken) =>
        {
            try
            {
                await auth.ResetPasswordAsync(new ResetPasswordInput(request.Token, request.NewPassword), cancellationToken);
            }
            catch (ValidationException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AuthException ex) when (ex.Code == AuthException.TokenInvalid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Link not valid", ApiError.Codes.TokenInvalid, ex.Message);
            }

            return Results.NoContent();
        });
    }
}
