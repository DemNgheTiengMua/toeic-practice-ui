using Microsoft.Extensions.FileProviders;
using WebAPI;
using WebAPI.Extensions;
using WebAPI.Utilities;

// 1. Environment bootstrap
EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

// 2. Database, ProblemDetails, Auth, RateLimiting, OpenAPI
builder.Services.AddCompanionDbContext(builder.Configuration);
builder.Services.AddApiProblemDetails();
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Environment);
builder.Services.AddApiDocumentation(builder.Configuration);

// 3. Domain services, AI grading, and payment gateways
builder.Services.AddDomainServices(builder.Configuration, builder.Environment);

var app = builder.Build();

// 4. Exception handling, static seed media (/audio, /img), and health diagnostics
app.UseApiProblemDetails();
foreach (var folder in new[] { "audio", "img" })
{
    var publishedDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot", folder);
    var repoPublicDir = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "web", "public", folder));
    var dir = Directory.Exists(publishedDir) ? publishedDir : Directory.Exists(repoPublicDir) ? repoPublicDir : null;
    if (dir is not null)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(dir),
            RequestPath = "/" + folder,
        });
    }
}
app.MapHealthChecks("/healthz");

// 5. Auth and rate limiting pipeline
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// 6. Endpoint routing
app.MapApiEndpoints();

// 7. OpenAPI document, Scalar UI, demo sign-in — all three behind OpenApi:Enabled
app.MapApiDocumentation(app.Configuration);

app.Run();

// For WebApplicationFactory-based tests
public partial class Program;
