using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #203: the studio's routes are mapped on the authoring capability, and the bundle they accept names
/// no exam. The endpoint table comes from the real <c>Program</c>, so a route that was never mapped
/// fails here rather than passing a gate over an empty set.
/// </summary>
public sealed class StimulusStudioRoutesTests : IClassFixture<StimulusStudioRoutesTests.Site>
{
    private readonly Site site;

    public StimulusStudioRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("GET", "api/admin/studio/stimulus/options")]
    [InlineData("POST", "api/admin/studio/stimulus/analyse")]
    [InlineData("POST", "api/admin/studio/stimulus")]
    public void The_studio_routes_are_mapped_on_the_authoring_capability(string method, string route)
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

        Assert.Contains(Permissions.ContentAuthor, policies);
        Assert.All(policies, policy => Assert.Contains(policy, Permissions.All));
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a request that carried a tier or a
    /// lifecycle state would let an author mint <c>production</c> stock or a <c>validated</c> item
    /// straight through the studio, which is INV-6 and CNT-42 in one field.
    /// </summary>
    [Fact]
    public void The_draft_request_carries_no_exam_no_tier_and_no_status()
    {
        var names = typeof(StimulusDraftRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("ExamId", names);
        Assert.DoesNotContain("SectionId", names);
        Assert.DoesNotContain("CertId", names);
        Assert.DoesNotContain("ContentTier", names);
        Assert.DoesNotContain("LifecycleStatus", names);
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
