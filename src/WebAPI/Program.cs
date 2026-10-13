using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Services;
using WebAPI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CompanionDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CompanionDb")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CompanionDbContext>();

// One token shape: HS256, 8-hour lifetime, no refresh token (issue #4). The signing key comes
// from configuration ("Jwt:SigningKey", base64); in development it lives in user-secrets.
// Config is parsed once, up front, with a friendly error — a malformed key must not surface
// as a raw FormatException on first use.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
const string jwtConfigHelp =
    "Configure the JWT in the \"Jwt\" section. In development: " +
    "dotnet user-secrets set \"Jwt:SigningKey\" <base64-32-bytes> --project src/WebAPI (plus Jwt:Issuer and Jwt:Audience).";
byte[] signingKeyBytes;
try
{
    signingKeyBytes = Convert.FromBase64String(jwtOptions.SigningKey ?? "");
}
catch (FormatException ex)
{
    throw new InvalidOperationException("Jwt:SigningKey is not valid base64. " + jwtConfigHelp, ex);
}
if (string.IsNullOrEmpty(jwtOptions.Issuer) || string.IsNullOrEmpty(jwtOptions.Audience))
{
    throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required. " + jwtConfigHelp);
}
if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must decode to at least 32 bytes (256 bits) for HS256. " + jwtConfigHelp);
}
builder.Services.AddSingleton(Options.Create(jwtOptions));
builder.Services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = IAccessTokenFactory.ClaimRole,
        };
        // A locked account is refused on every request, not only at sign-in: re-check the live
        // row on each token validation. Auth failures get a ProblemDetails body with a stable
        // code here, because the exception-handler middleware never sees auth rejections.
        static Task WriteAuthProblem(HttpContext http, int status, string title, string code)
        {
            var problem = new ProblemDetails { Title = title, Status = status };
            problem.Extensions["code"] = code;
            http.Response.StatusCode = status;
            return http.Response.WriteAsJsonAsync(problem);
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                var idClaim = context.Principal?.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value;
                if (!int.TryParse(idClaim, out var userId))
                {
                    context.Fail("Token carries no usable user id.");
                    return;
                }

                if (!await auth.IsActiveAsync(userId, context.HttpContext.RequestAborted))
                {
                    context.Fail("Account is locked or no longer exists.");
                    // Items, not a response header: this is server-internal plumbing between events.
                    context.HttpContext.Items["auth-failure"] = ApiError.Codes.Locked;
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var code = context.HttpContext.Items["auth-failure"] as string == ApiError.Codes.Locked
                    ? ApiError.Codes.Locked
                    : ApiError.Codes.Unauthenticated;
                var title = code == ApiError.Codes.Locked ? "Account locked" : "Authentication required";
                await WriteAuthProblem(context.HttpContext, StatusCodes.Status401Unauthorized, title, code);
            },
            OnForbidden = context =>
                WriteAuthProblem(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden", ApiError.Codes.Forbidden),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin));
// Every failure — thrown exception or bare status-code result — leaves the API as RFC 9457
// ProblemDetails with a stable machine-readable "code" extension the MVC site switches on.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
        {
            StatusCodes.Status404NotFound => ApiError.Codes.NotFound,
            _ => ApiError.Codes.Unexpected,
        };
    });
builder.Services.AddScoped<ICertificateService, CertificateService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

// Every unhandled failure leaves the API as RFC 9457 ProblemDetails with a stable code.
app.UseExceptionHandler(errorApp =>
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
        };
        problem.Extensions["code"] = ApiError.Codes.Unexpected;
        await context.Response.WriteAsJsonAsync(problem);
    }));

app.MapHealthChecks("/healthz");

app.UseAuthentication();
app.UseAuthorization();

// Bare status-code results get a ProblemDetails body. Bearer-auth 401/403s are answered by the
// JWT events above (OnChallenge / OnForbidden), which run first; this fallback fires for 404s
// and any other status — including a hypothetical future non-bearer 401/403, which would then
// get the generic body rather than a precise auth code.
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
    {
        return;
    }

    var response = context.HttpContext.Response;
    var problem = new ProblemDetails
    {
        Title = status == StatusCodes.Status404NotFound ? "Not Found" : "Error",
        Status = status,
    };
    problem.Extensions["code"] = status == StatusCodes.Status404NotFound
        ? ApiError.Codes.NotFound
        : ApiError.Codes.Unexpected;
    await response.WriteAsJsonAsync(problem);
});

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
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
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
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
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
});

// Returns the live database row, not the token's claims: the MVC site's cookie revalidation
// compares against this to catch server-side changes (role, name) since sign-in.
app.MapGet("/api/auth/me", async Task<IResult> (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
{
    var userId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
    var user = await auth.GetUserAsync(userId, cancellationToken);
    if (user is null)
    {
        return Problem(StatusCodes.Status404NotFound, "Not Found", ApiError.Codes.NotFound);
    }

    return Results.Ok(new AuthUserDto(user.UserId, user.Email, user.FullName, user.Role));
}).RequireAuthorization();

// ---- Profile (issue #6) ----

app.MapGet("/api/profile", async (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
{
    var userId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
    var profile = await auth.GetProfileAsync(userId, cancellationToken);
    return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
}).RequireAuthorization();

app.MapPut("/api/profile", async Task<IResult> (
    UpdateProfileRequest request,
    HttpContext http,
    IAuthService auth,
    CancellationToken cancellationToken) =>
{
    var userId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
    try
    {
        var profile = await auth.UpdateProfileAsync(
            userId, new UpdateProfileInput(request.FullName, request.Phone, request.TimeZoneId), cancellationToken);
        return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
    }
    catch (ValidationException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
    }
}).RequireAuthorization();

app.MapPost("/api/profile/password", async Task<IResult> (
    ChangePasswordRequest request,
    HttpContext http,
    IAuthService auth,
    CancellationToken cancellationToken) =>
{
    var userId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
    try
    {
        await auth.ChangePasswordAsync(
            userId, new ChangePasswordInput(request.CurrentPassword, request.NewPassword), cancellationToken);
        return Results.NoContent();
    }
    catch (ValidationException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
    }
    catch (AuthException ex) when (ex.Code == AuthException.InvalidCredentials)
    {
        return Problem(StatusCodes.Status400BadRequest, "Password change failed", ApiError.Codes.InvalidCredentials, ex.Message);
    }
}).RequireAuthorization();

// Demo of the admin-only policy: admin pages and endpoints refuse a learner.
app.MapGet("/api/admin/summary", (HttpContext http) =>
    Results.Ok(new { admin = http.User.FindFirst(IAccessTokenFactory.ClaimEmail)!.Value }))
    .RequireAuthorization(Roles.Admin);

static IResult ToTokenResponse(AuthUser user, IAccessTokenFactory tokens)
{
    var token = tokens.Create(user.UserId, user.Email, user.Role, user.FullName);
    return Results.Ok(new AuthTokenResponse(
        token.Token, token.ExpiresAt,
        new AuthUserDto(user.UserId, user.Email, user.FullName, user.Role)));
}

static IResult Problem(int status, string title, string code, string? detail = null)
{
    var problem = new ProblemDetails { Title = title, Status = status, Detail = detail };
    problem.Extensions["code"] = code;
    return Results.Json(problem, statusCode: status);
}

app.MapGet("/api/certificates", async (ICertificateService certificates, CancellationToken cancellationToken) =>
{
    var catalog = await certificates.GetCatalogAsync(cancellationToken);
    var dtos = catalog.Select(c => new CertificateDto(
        c.Code,
        c.Name,
        c.Description,
        c.Skills.Select(s => new SkillDto(
            s.Code,
            s.Name,
            s.Modality,
            s.DurationMinutes,
            s.Sections.Select(sec => new SectionDto(sec.Code, sec.Name, sec.ExpectedQuestionCount)).ToList())).ToList(),
        c.Bands.Select(b => new BandDto(b.Code, b.Name, b.MinTotal, b.MaxTotal)).ToList()));
    return Results.Ok(dtos);
});

app.Run();

// For WebApplicationFactory-based tests in later slices.
public partial class Program;
