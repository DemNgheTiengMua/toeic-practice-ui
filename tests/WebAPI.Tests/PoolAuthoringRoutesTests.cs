using System.Reflection;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #188: the pool authoring routes are mapped, they sit on the authoring capability, and none of
/// them names an exam except the binding. The endpoint table comes from the real <c>Program</c>, so
/// a route that was never mapped fails here rather than passing <c>StaffEndpointGatesTests</c>
/// vacuously - a gate over an empty set is not a gate.
/// </summary>
public sealed class PoolAuthoringRoutesTests : IClassFixture<PoolAuthoringRoutesTests.Site>
{
    private readonly Site site;

    public PoolAuthoringRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("POST", "api/admin/items")]
    [InlineData("PUT", "api/admin/items/{itemId:int}")]
    [InlineData("POST", "api/admin/exams/{examId:int}/bindings")]
    [InlineData("GET", "api/admin/exams/{examId:int}/assembly")]
    [InlineData("GET", "api/admin/ingestion/{batchId:int}")]
    [InlineData("POST", "api/admin/ingestion/{batchId:int}/records/{line:int}/resubmit")]
    [InlineData("POST", "api/admin/ingestion/{batchId:int}/records/{line:int}/discard")]
    [InlineData("POST", "api/admin/ingestion/{batchId:int}/items/{itemId:int}/clear")]
    public void The_authoring_routes_are_mapped_on_the_authoring_capability(string method, string route)
    {
        var endpoint = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(e => e.RoutePattern.RawText?.TrimStart('/') == route
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        Assert.True(endpoint is not null, $"{method} /{route} is not mapped");

        var policies = endpoint!.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .OfType<string>()
            .ToList();

        // #207: clearing is review, not authoring; every other workbench route is authoring.
        Assert.Contains(
            route.EndsWith("/clear", StringComparison.Ordinal) ? Permissions.ContentReview : Permissions.ContentAuthor,
            policies);
        Assert.All(policies, policy => Assert.Contains(policy, Permissions.All));
        Assert.All(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            a => Assert.True(string.IsNullOrEmpty(a.Roles), $"({method} /{route}) is gated on role names"));
    }

    /// <summary>
    /// The shape that keeps AC1 honest: the request an author posts has no exam field, so there is
    /// nothing for the API to attach the item to even if a caller wanted it to.
    /// </summary>
    [Fact]
    public void The_create_request_carries_no_exam_reference()
    {
        var names = typeof(CreatePoolItemRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("ExamId", names);
        Assert.DoesNotContain("SectionId", names);
        Assert.DoesNotContain("CertId", names);
        // And no tier knob either: a new item is demo until review promotes it (INV-6).
        Assert.DoesNotContain("ContentTier", names);
    }

    /// <summary>
    /// #265: the legacy question and group authoring routes are GONE, not empty.
    /// <para>
    /// They wrote <c>Questions</c>, <c>QuestionGroups</c> and <c>QuestionOptions</c>, which #269 drops.
    /// An endpoint whose backing table is deleted must not answer at all, so the assertion is absence
    /// from the route table rather than a 404 at request time: a route that exists and 404s for another
    /// reason would pass a request-level check and still be a door.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("POST", "api/admin/exams/{examId:int}/questions")]
    [InlineData("PUT", "api/admin/questions/{questionId:int}")]
    [InlineData("DELETE", "api/admin/questions/{questionId:int}")]
    [InlineData("POST", "api/admin/exams/{examId:int}/groups")]
    [InlineData("PUT", "api/admin/exams/{examId:int}/groups/{groupId:int}")]
    [InlineData("DELETE", "api/admin/exams/{examId:int}/groups/{groupId:int}")]
    public void The_legacy_authoring_routes_are_not_mapped(string method, string route)
    {
        var mapped = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/') == route
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .ToList();

        Assert.Empty(mapped);
    }

    /// <summary>
    /// The read the editor still needs is mapped. Asserting the positive here is what keeps the theory
    /// above from passing on a Program that mapped nothing at all.
    /// </summary>
    [Fact]
    public void The_editor_read_is_still_mapped()
    {
        var mapped = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(e => e.RoutePattern.RawText?.TrimStart('/') == "api/admin/exams/{examId:int}/questions"
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        Assert.True(mapped, "GET api/admin/exams/{examId}/questions must still answer");
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
