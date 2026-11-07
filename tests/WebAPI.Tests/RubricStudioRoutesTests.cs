using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #208: the rubric studio's four routes are mapped, on a real capability, over the real
/// <c>Program</c>. <b>Which guard could this pass while being wrong:</b> testing the service instead of
/// the route - a route typo is invisible to a service test, and the screen would 404 the moment a staff
/// member opened it.
/// </summary>
public sealed class RubricStudioRoutesTests : IClassFixture<RubricStudioRoutesTests.Site>
{
    private readonly Site site;

    public RubricStudioRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("GET", "api/admin/studio/rubric")]
    [InlineData("POST", "api/admin/studio/rubric")]
    [InlineData("POST", "api/admin/studio/rubric/ladder")]
    [InlineData("POST", "api/admin/studio/rubric/lock")]
    public void The_studio_routes_are_mapped_on_the_blueprint_capability(string method, string route)
    {
        var endpoint = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText?.TrimStart('/') == route
                && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        Assert.True(endpoint is not null, $"{method} /{route} is not mapped");

        var policies = endpoint!.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorize => authorize.Policy)
            .OfType<string>()
            .ToList();

        // The rubric is what the grader reads, so it belongs to the authoring capability - beside the
        // two content studios, not under blueprint design's certificate scoring shape.
        Assert.Contains(Permissions.ContentAuthor, policies);
        Assert.All(policies, policy => Assert.Contains(policy, Permissions.All));
    }

    /// <summary>
    /// The lock takes the SAME request as the ladder run, because it re-runs the ladder rather than
    /// trusting a verdict a browser posted. <b>Which guard could this pass while being wrong:</b> a lock
    /// endpoint that accepted a stored outcome - a browser is the one place a claim must never be read
    /// from.
    /// </summary>
    [Fact]
    public void The_lock_accepts_the_ladder_request_and_not_a_posted_verdict()
    {
        var properties = typeof(RunRubricLadderRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.Contains("Responses", properties);
        // Nothing on the request can carry a verdict, so the endpoint has to compute one.
        Assert.DoesNotContain("Outcome", properties);
        Assert.DoesNotContain("Accepted", properties);
        Assert.DoesNotContain("RuleCode", properties);
    }

    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Jwt:Issuer", "webapi-tests");
            builder.UseSetting("Jwt:Audience", "webapi-tests");
        }
    }
}
