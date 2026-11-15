using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AuthEndpoints
{
    private static void MapRegistration(IEndpointRouteBuilder app)
    {
        // ---- Auth ----

        app.MapPost("/api/auth/register", async Task<IResult> (
            RegisterRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            AuthUser user;
            try
            {
                user = await auth.RegisterAsync(
                    new RegisterInput(request.FullName, request.Email, request.Phone, request.Password, request.TermsAccepted),
                    cancellationToken);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (AuthException ex) when (ex.Code == AuthException.DuplicateEmail)
            {
                return Problem(StatusCodes.Status409Conflict, "Email already registered", ApiError.Codes.EmailTaken, ex.Message);
            }

            // ADR-0018: 201 with no session. Verification gates sign-in, so handing back a token here
            // would mint a credential for an account that cannot use it. The caller opens the link.
            return Results.Created($"/api/auth/me", new { userId = user.UserId, email = user.Email });
        });
    }
}
