using System.Linq;
using System.Reflection;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #222: the mock projection is two routes, not one, and the verbs are the boundary.
/// <para>
/// The read is a GET and the sync is a POST. That is not cosmetic: a single endpoint that returned the
/// projection and wrote it when asked would leave the write reachable from a page load, which is the
/// defect OQ-TR-01 exists to prevent. The endpoint table comes from the real <c>Program</c>, so a route
/// that was never mapped fails here rather than passing on an empty set.
/// </para>
/// </summary>
public sealed class MockProjectionRoutesTests : IClassFixture<MockProjectionRoutesTests.Site>
{
    private readonly Site site;

    public MockProjectionRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("GET", "api/learner/attempts/{attemptId:int}/projection")]
    [InlineData("POST", "api/learner/attempts/{attemptId:int}/projection/sync")]
    public void The_projection_routes_are_mapped_with_the_verb_that_says_whether_they_write(string method, string route)
    {
        var endpoint = Find(route);

        Assert.True(endpoint is not null, $"{method} /{route} is not mapped");
        Assert.Contains(method, endpoint!.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
    }

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> the sync mapped as a GET - or as a POST
    /// that is also reachable by GET. A read that writes is the defect; the distinct paths and verbs are
    /// what keep the two apart, and the DTO the sync answers with is the standing, not the projection.
    /// </summary>
    [Fact]
    public void The_sync_is_a_post_and_the_read_is_not()
    {
        var read = Find("api/learner/attempts/{attemptId:int}/projection")!;
        var sync = Find("api/learner/attempts/{attemptId:int}/projection/sync")!;

        Assert.DoesNotContain("POST", read.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.DoesNotContain("GET", sync.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.DoesNotContain("PUT", sync.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
    }

    /// <summary>
    /// PRN-03, on the wire: nothing in the result DTO lets the paper's certificate score carry a level.
    /// The per-skill entry has a level and the certificate total does not, which is the shape that makes
    /// "TOEIC 785-940 is B2" unrepresentable rather than merely discouraged.
    /// </summary>
    [Fact]
    public void The_wire_result_has_no_level_on_the_certificate_score()
    {
        var members = typeof(MockResultProjectionDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("LevelCode", members, System.StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CefrLevel", members, System.StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CertificateScore", members);
        Assert.Contains("BandCode", members);

        // The per-skill row is where a level lives, and its level is nullable so a gap is not a floor.
        var level = typeof(MockSkillProjectionDto).GetProperty("AwardedLevelCode")!;
        Assert.Equal(typeof(string), level.PropertyType);
    }

    private RouteEndpoint? Find(string route) =>
        site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(e => e.RoutePattern.RawText?.TrimStart('/') == route);

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
