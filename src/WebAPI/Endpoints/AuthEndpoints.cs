using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Auth ----

        app.MapPost("/api/auth/register", async Task<IResult> (
            RegisterRequest request,
            IAuthService auth,
            IAccessTokenFactory tokens,
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

            return ToTokenResponse(user, tokens);
        });

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
