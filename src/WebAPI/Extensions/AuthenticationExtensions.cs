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
                        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                        {
                            // The browser navigated away or stopped buffering mid-request, so
                            // RequestAborted fired the session query's token. That is a normal
                            // disconnect, not a token rejection: let validation pass and leave
                            // the status to the exception handler, which already answers 499 for
                            // aborted requests. Failing here instead would raise a challenge that
                            // JwtBearerHandler logs as "Exception occurred while processing
                            // message" — the fail: noise of F4.
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
}
