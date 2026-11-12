using System.Net;
using System.Net.Http.Json;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// A caller who forgets a required query parameter is told the request is wrong, not that the server
/// is broken. Minimal APIs throw <see cref="BadHttpRequestException"/> for a missing or unparseable
/// parameter, and .NET 10 does not carry its status into problem details (ASP.NET Core 11 does), so
/// without the handler preserving it every such request answered 500 UNEXPECTED and wrote a defect
/// into the log that never existed.
/// <para>
/// Mounts the real <see cref="ProblemDetailsExtensions.UseApiProblemDetails"/> middleware over a real
/// minimal-API endpoint, so this asserts production behaviour and not a reimplementation of it.
/// </para>
/// </summary>
public sealed class BadRequestBindingTests
{
    [Fact]
    public async Task A_missing_required_query_parameter_answers_400()
    {
        var (client, app) = await AppAsync();
        await using (app)
        {
            var response = await client.GetAsync("/probe");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_400_is_a_validation_problem_not_a_server_error()
    {
        var (client, app) = await AppAsync();
        await using (app)
        {
            var problem = await (await client.GetAsync("/probe")).Content.ReadFromJsonAsync<ProblemShape>();

            Assert.Equal(400, problem!.Status);
            Assert.Equal(ApiError.Codes.Validation, problem.Code);
        }
    }

    [Fact]
    public async Task An_unexpected_exception_still_answers_500()
    {
        var (client, app) = await AppAsync();
        await using (app)
        {
            var response = await client.GetAsync("/probe-boom?level=B1");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
    }

    private sealed record ProblemShape(string? Title, int? Status, string? Code);

    private static async Task<(HttpClient Client, WebApplication App)> AppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        // ThrowOnBadRequest is the Development default; set it explicitly so the failure is exercised
        // regardless of the environment the suite happens to run in.
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

        var app = builder.Build();
        app.UseApiProblemDetails();

        // The shape under test: a required string parameter, exactly like LessonEndpoints' `level`.
        app.MapGet("/probe", (string level) => Results.Ok(level));
        app.MapGet("/probe-boom", (string level) =>
        {
            _ = level;
            throw new InvalidOperationException("boom");
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }
}
