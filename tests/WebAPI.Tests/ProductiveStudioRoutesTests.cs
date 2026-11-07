using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #204: the productive studio's routes are mapped on the authoring capability, and the task they
/// accept names no exam. The endpoint table comes from the real <c>Program</c>, so a route that was
/// never mapped fails here rather than passing a gate over an empty set.
/// </summary>
public sealed class ProductiveStudioRoutesTests : IClassFixture<ProductiveStudioRoutesTests.Site>
{
    private readonly Site site;

    public ProductiveStudioRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("GET", "api/admin/studio/productive/options")]
    [InlineData("POST", "api/admin/studio/productive/analyse")]
    [InlineData("POST", "api/admin/studio/productive")]
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
    /// lifecycle state would let an author mint <c>production</c> stock or a <c>validated</c> task
    /// straight through the studio, which is INV-6 and CNT-42 in one field. And a request field for
    /// placement eligibility is deliberate - D0.2's cap depends on it - so it must stay a bool rather
    /// than a status an author could flip into promotion.
    /// </summary>
    [Fact]
    public void The_draft_request_carries_no_exam_no_tier_and_no_status()
    {
        var names = typeof(ProductiveTaskDraftRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("ExamId", names);
        Assert.DoesNotContain("SectionId", names);
        Assert.DoesNotContain("CertId", names);
        Assert.DoesNotContain("ContentTier", names);
        Assert.DoesNotContain("LifecycleStatus", names);

        // And the placement flag it does carry is a bool, not the lifecycle vocabulary in disguise.
        var placement = typeof(ProductiveTaskDraftRequest).GetProperty("PlacementEligible");
        Assert.NotNull(placement);
        Assert.Equal(typeof(bool), placement!.PropertyType);
    }

    /// <summary>
    /// A sub-task carries its own demand: the word range and both windows are per part, because D0.7
    /// weights Task 2 twice Task 1. A shape that put them on the task would make a two-part task unable
    /// to describe its second part.
    /// </summary>
    [Fact]
    public void A_sub_task_carries_its_own_word_range_and_windows()
    {
        var names = typeof(ProductiveSubTaskInput)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.Contains("MinWords", names);
        Assert.Contains("MaxWords", names);
        Assert.Contains("PrepSeconds", names);
        Assert.Contains("ResponseSeconds", names);

        var taskNames = typeof(ProductiveTaskDraftRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        // Never on the task: one pair for two parts is the defect.
        Assert.DoesNotContain("MinWords", taskNames);
        Assert.DoesNotContain("MaxWords", taskNames);
        Assert.DoesNotContain("ResponseSeconds", taskNames);
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
