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

                document.Tags = new HashSet<OpenApiTag>
                {
                    new() { Name = "Authentication & Profile", Description = "User authentication, profile management, password recovery, and session tokens." },
                    new() { Name = "Learner Dashboard & Context", Description = "Active certificate context, onboarding state, and learner metrics." },
                    new() { Name = "Mock Exams & Attempts", Description = "Full-length standardized mock exams, timer lifecycle, answer persistence, and score scaling." },
                    new() { Name = "Universal Placement", Description = "Diagnostic placement exam sittings (2 or 4 skills) and baseline band calibration." },
                    new() { Name = "Learning Path & Sprints", Description = "4-week deterministic milestone learning sprints and step progression." },
                    new() { Name = "Practice Drilling", Description = "Untimed topic and section review drills with instant feedback." },
                    new() { Name = "Speaking Practice & AI Grading", Description = "Speaking audio recording, audio submission, and asynchronous AI acoustic/transcript grading." },
                    new() { Name = "Writing Practice & AI Grading", Description = "Writing draft autosave, essay submission, and asynchronous AI rubric grading." },
                    new() { Name = "Skill Practice", Description = "Direct skill modality drilling and answer evaluation." },
                    new() { Name = "Lessons & Study Material", Description = "Theory lessons, concept cards, and category study materials." },
                    new() { Name = "Mentors & Bookings", Description = "Human mentor profiles, weekly availability windows, and credit-based session bookings." },
                    new() { Name = "Wallet & Orders", Description = "Credit purchasing, PayOS embedded checkout orders, and transaction receipts." },
                    new() { Name = "AI Insights", Description = "Fact-grounded learner accuracy analytics and LLM study recommendations." },
                    new() { Name = "Weakness Analysis", Description = "Deterministic section and topic accuracy views and wrong-answer fingerprints." },
                    new() { Name = "Saved Questions", Description = "Learner bookmarked questions and review notes." },
                    new() { Name = "Catalog & Certificates", Description = "Public certificates, sections, score scales, and band hierarchies." },
                    new() { Name = "Learner Complaints", Description = "Learner question feedback and item complaints." },
                    new() { Name = "Admin - Content Studio & Blueprints", Description = "Exam papers, question banks, section blueprints, and rubric criteria authoring." },
                    new() { Name = "Admin - Question Imports", Description = "Batch question ZIP importing and media assets." },
                    new() { Name = "Admin - Lesson Authoring", Description = "Theory lesson creation and concept card management." },
                    new() { Name = "Admin - Mentor Management", Description = "Staff mentor administration, meeting links, and schedules." },
                    new() { Name = "Admin - Operations & Governance", Description = "Platform executive dashboard, user governance, orders, and system health." },
                    new() { Name = "Admin - Support & Complaints", Description = "Support triage for question disputes and learner complaints." },
                    new() { Name = "Admin - Refunds", Description = "Credit and payment refund processing." },
                    new() { Name = "Health & Diagnostics", Description = "Application health status and ping." },
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

                var tagName = DeriveTagFromPath(context.Description.RelativePath);
                operation.Tags = new HashSet<OpenApiTagReference>
                {
                    new(tagName, context.Document!)
                };

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

    private static string DeriveTagFromPath(string? path)
    {
        var normalized = (path ?? string.Empty).TrimStart('/').ToLowerInvariant();

        if (normalized.StartsWith("api/auth") || normalized.StartsWith("api/profile"))
            return "Authentication & Profile";

        if (normalized.StartsWith("api/admin/imports"))
            return "Admin - Question Imports";
        if (normalized.StartsWith("api/admin/lessons"))
            return "Admin - Lesson Authoring";
        if (normalized.StartsWith("api/admin/mentors"))
            return "Admin - Mentor Management";
        if (normalized.StartsWith("api/admin/complaints"))
            return "Admin - Support & Complaints";
        if (normalized.StartsWith("api/admin/refunds"))
            return "Admin - Refunds";
        if (normalized.StartsWith("api/admin/studio")
            || normalized.StartsWith("api/admin/exams")
            || normalized.StartsWith("api/admin/questions")
            || normalized.StartsWith("api/admin/items")
            || normalized.StartsWith("api/admin/coverage")
            || normalized.StartsWith("api/admin/quick-ingest")
            || normalized.StartsWith("api/admin/certificates")
            || normalized.StartsWith("api/admin/score-conversion"))
            return "Admin - Content Studio & Blueprints";
        if (normalized.StartsWith("api/admin"))
            return "Admin - Operations & Governance";

        if (normalized.StartsWith("api/learner/exams")
            || normalized.StartsWith("api/learner/attempts")
            || normalized.StartsWith("api/exams"))
            return "Mock Exams & Attempts";

        if (normalized.StartsWith("api/learner/placement")
            || normalized.StartsWith("api/placement"))
            return "Universal Placement";

        if (normalized.StartsWith("api/learner/learning-path"))
            return "Learning Path & Sprints";

        if (normalized.StartsWith("api/learner/speaking"))
            return "Speaking Practice & AI Grading";

        if (normalized.StartsWith("api/learner/writing"))
            return "Writing Practice & AI Grading";

        if (normalized.StartsWith("api/learner/skill-practice"))
            return "Skill Practice";

        if (normalized.StartsWith("api/learner/practice"))
            return "Practice Drilling";

        if (normalized.StartsWith("api/learner/lessons") || normalized.StartsWith("lessons"))
            return "Lessons & Study Material";

        if (normalized.StartsWith("api/learner/insights"))
            return "AI Insights";

        if (normalized.StartsWith("api/learner/weakness"))
            return "Weakness Analysis";

        if (normalized.StartsWith("api/learner/saved-questions"))
            return "Saved Questions";

        if (normalized.StartsWith("api/learner/complaints"))
            return "Learner Complaints";

        if (normalized.StartsWith("api/learner"))
            return "Learner Dashboard & Context";

        if (normalized.StartsWith("api/mentors"))
            return "Mentors & Bookings";

        if (normalized.StartsWith("api/checkout") || normalized.StartsWith("api/orders"))
            return "Wallet & Orders";

        if (normalized.StartsWith("api/catalog"))
            return "Catalog & Certificates";

        if (normalized.StartsWith("healthz"))
            return "Health & Diagnostics";

        return "General";
    }
}
