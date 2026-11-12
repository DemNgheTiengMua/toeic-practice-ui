using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Microsoft.OpenApi;
using Services;
using Scalar.AspNetCore;
using WebAPI.Endpoints;

namespace WebAPI.Extensions;

/// <summary>
/// #285: the OpenAPI document, its browsable UI and a demo sign-in behind one flag.
///
/// <para>
/// <b>The demo sign-in is a complete back door.</b> Anyone who can reach <c>POST /api/auth/demo-sign-in</c>
/// gets a signed token for the configured demo account — the password is in
/// <c>docs/MANUAL-TEST-CREDENTIALS.md</c> and in this repository's history, so it is not a secret. That is
/// acceptable for a reviewer on a laptop and unacceptable on the public internet. Deploy with
/// <c>OpenApi:Enabled=false</c>: it removes the document, the UI and this endpoint together, which is why
/// the three live in one file.
/// </para>
/// </summary>
public static class OpenApiExtensions
{
    public const string SectionName = "OpenApi";

    public const string DemoSignInPath = "/api/auth/demo-sign-in";

    /// <summary>The scheme name the document declares and the UI pre-selects. One spelling, two places.</summary>
    private const string BearerScheme = "BearerAuth";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return services;
        }

        services.AddOpenApi("v1", options =>
        {
            // The document lists routes and their authentication requirement, which is all a reviewer
            // needs to navigate the API. Response bodies are deliberately absent: every handler returns
            // an untyped IResult union, so describing them would mean editing all 239 handlers.
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = $"Token from POST /api/auth/sign-in, or {DemoSignInPath} while this flag is on.",
                };
                return Task.CompletedTask;
            });

            // Growing the document by hand would drift the moment a route is added, so the scheme is
            // attached from the endpoint's own authorization metadata instead of a list of routes.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var requiresAuth = context.Description.ActionDescriptor?.EndpointMetadata
                    ?.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any() ?? false;
                if (requiresAuth)
                {
                    operation.Security ??= new List<OpenApiSecurityRequirement>();
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, context.Document!, null)] = new List<string>(),
                    });
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static WebApplication MapApiDocumentation(this WebApplication app, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return app;
        }

        app.MapOpenApi();

        // Same origin as the document (this solution has no CORS policy at all, ADR-0002), so Scalar's
        // relative default of /openapi/v1.json is correct and no server URL is set.
        //
        // The token is minted per page load rather than left for the reviewer to paste: that is what
        // "one click" buys. It signs in through IAuthService, so it takes the same password check as
        // /api/auth/sign-in, and a missing or locked demo account degrades to a page with no token
        // rather than a 500.
        //
        // shortcut: each /scalar page load pays one password verification and is not rate-limited,
        // because it is a service call rather than a request to /api/auth/sign-in. That is a cheap
        // hash per page view, not a brute-force surface (the password is correct), so it is left
        // alone. Add a cached token if /scalar ever becomes a hot path.
        app.MapScalarApiReference(async (options, httpContext) =>
        {
            options
                .WithTitle("Extra Efficiency API")
                .AddPreferredSecuritySchemes(BearerScheme)
                .EnablePersistentAuthentication();

            var token = await TryMintDemoTokenAsync(httpContext);
            if (token is not null)
            {
                options.AddHttpAuthentication(BearerScheme, scheme => scheme.WithToken(token));
            }
        });

        app.MapDemoSignIn(configuration);

        return app;
    }

    /// <summary>The demo account the flag's sign-in uses. One place, so the page and the route never disagree.</summary>
    private static SignInInput DemoCredentials(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new SignInInput(
            section["DemoEmail"] ?? "admin@demo.local",
            section["DemoPassword"] ?? "Demo1234!");
    }

    private static async Task<string?> TryMintDemoTokenAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;

        try
        {
            var user = await services.GetRequiredService<IAuthService>().SignInAsync(
                DemoCredentials(services.GetRequiredService<IConfiguration>()),
                httpContext.RequestAborted);
            return services.GetRequiredService<IAccessTokenFactory>()
                .Create(user.UserId, user.Email, user.Role, user.FullName, user.SessionStamp, user.Permissions)
                .Token;
        }
        catch (Exception) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            // A wrong demo password, a locked account or an unreachable database all land here. The
            // documentation page must still render; the reviewer then sees the real error by calling
            // /api/auth/demo-sign-in, which reports it instead of hiding it.
            return null;
        }
    }

    /// <summary>
    /// Mints a token for the configured demo account so a reviewer never handles one by hand. It signs in
    /// through <see cref="IAuthService"/>, so the password is verified by the same code as the real route
    /// and a changed demo password breaks this instead of silently bypassing it.
    /// </summary>
    private static void MapDemoSignIn(this WebApplication app, IConfiguration configuration) =>
        app.MapPost(DemoSignInPath, async Task<IResult> (
            IAuthService auth,
            IAccessTokenFactory tokens,
            CancellationToken cancellationToken) =>
        {
            AuthUser user;
            try
            {
                user = await auth.SignInAsync(DemoCredentials(configuration), cancellationToken);
            }
            catch (ValidationException ex)
            {
                return EndpointHelpers.ValidationProblem(ex);
            }
            catch (AuthException ex)
            {
                return EndpointHelpers.Problem(
                    StatusCodes.Status401Unauthorized, "Demo sign-in failed", ex.Code, ex.Message);
            }

            return EndpointHelpers.ToTokenResponse(user, tokens);
        }).RequireRateLimiting("auth");

    private static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue($"{SectionName}:Enabled", true);
}
