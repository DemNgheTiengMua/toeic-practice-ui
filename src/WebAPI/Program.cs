using Contracts;
using DataAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CompanionDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CompanionDb")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CompanionDbContext>();
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

// Bare status-code results (404 for an unknown route, etc.) get the same ProblemDetails body;
// the no-arg UseStatusCodePages() overload emits plain text, which would skip the "code".
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    var problem = new ProblemDetails
    {
        Title = response.StatusCode == StatusCodes.Status404NotFound ? "Not Found" : "Error",
        Status = response.StatusCode,
    };
    problem.Extensions["code"] = response.StatusCode == StatusCodes.Status404NotFound
        ? ApiError.Codes.NotFound
        : ApiError.Codes.Unexpected;
    await response.WriteAsJsonAsync(problem);
});

app.MapHealthChecks("/healthz");

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
