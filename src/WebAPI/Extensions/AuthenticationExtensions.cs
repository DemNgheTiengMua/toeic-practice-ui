using Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Services;

namespace WebAPI.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
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

        // The key below was committed to this public repository in 7aad51e and is public forever.
        // Rotation is only a fix if every instance actually stops accepting it, so refuse to boot
        // rather than run with a key anyone can read on GitHub.
        if (IsRetiredKey(signingKeyBytes))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is the key that leaked into the public repository history and is no longer accepted. " +
                "Generate a new one: dotnet user-secrets set \"Jwt:SigningKey\" \"$(openssl rand -base64 32)\" --project src/WebAPI " +
                "then restart. Signed-in users are signed out once and can sign straight back in.");
        }

        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

                        var stamp = context.Principal?.FindFirst(IAccessTokenFactory.ClaimSessionStamp)?.Value;
                        SessionStatus status;
                        try
                        {
                            status = await auth.CheckSessionAsync(userId, stamp, context.HttpContext.RequestAborted);
                        }
                        catch (Exception) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                        {
                            // The client disconnected while the session query ran, so RequestAborted
                            // cancelled it. SqlClient reports that as SqlException ("Operation
                            // cancelled by user") as often as OperationCanceledException, hence the
                            // broad filter. The session and lock checks never finished, so the token
                            // must not authenticate. Fail rather than rethrow: JwtBearerHandler logs
                            // "Exception occurred while processing message" only when this event
                            // throws, and nobody is left to read the 401.
                            context.Fail("Request aborted during the session check.");
                            return;
                        }

                        switch (status)
                        {
                            case SessionStatus.Locked:
                                context.Fail("Account is locked.");
                                context.HttpContext.Items["auth-failure"] = ApiError.Codes.Locked;
                                break;
                            case SessionStatus.Ended:
                                context.Fail("Account no longer exists or the password has changed.");
                                break;
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

        services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(Roles.Learner, policy => policy.RequireRole(Roles.Learner));

        return services;
    }

    /// <summary>
    /// The HMAC key material that was committed to this repository, hashed after base64 decoding so
    /// this file is not itself a copy of the secret. The guard hashes the <em>decoded</em> bytes, the
    /// same material <c>Convert.FromBase64String</c> hands the token validator: base64 tolerates
    /// whitespace and line breaks, so hashing the configured text would let a re-wrapped copy of the
    /// retired key through while still decoding to the same bytes.
    /// </summary>
    private static bool IsRetiredKey(byte[] candidateBytes)
    {
        const string retiredSha256 = "5a386fb1fdc2021174643b350731b460018fa920182ee7bae19658ee1a42dc5a";
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(candidateBytes))
            .ToLowerInvariant();
        return string.Equals(digest, retiredSha256, StringComparison.Ordinal);
    }
}
