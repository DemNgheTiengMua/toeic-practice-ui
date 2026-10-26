using WebAPI;
using WebAPI.Extensions;
using WebAPI.Utilities;

// 1. Environment bootstrap
EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

// 2. Database, ProblemDetails, Auth, RateLimiting
builder.Services.AddCompanionDbContext(builder.Configuration);
builder.Services.AddApiProblemDetails();
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Environment);

// 3. Domain services, AI grading, and payment gateways
builder.Services.AddDomainServices(builder.Configuration, builder.Environment);

var app = builder.Build();

// 4. Exception handling and health diagnostics
app.UseApiProblemDetails();
app.MapHealthChecks("/healthz");

// 5. Auth and rate limiting pipeline
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// 6. Endpoint routing
app.MapApiEndpoints();

// 7. CLI tool entry point
if (await ExamCliHandler.TryHandleAsync(app, args))
{
    return;
}

app.Run();

// For WebApplicationFactory-based tests
public partial class Program;
