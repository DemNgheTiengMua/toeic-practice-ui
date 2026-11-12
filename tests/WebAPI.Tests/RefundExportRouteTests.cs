using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #283: the export route is mapped, it sits on the same support capability as the rest of the refunds
/// screens, and it is a GET - the read that must be repeatable. Asserted against the real route table, so
/// a route that was never mapped fails here instead of passing the gate test vacuously.
/// </summary>
public sealed class RefundExportRouteTests : IClassFixture<RefundExportRouteTests.Site>
{
    private readonly Site site;

    public RefundExportRouteTests(Site site) => this.site = site;

    [Fact]
    public void The_export_is_a_get_on_the_support_capability()
    {
        var endpoint = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(e =>
                e.RoutePattern.RawText?.TrimStart('/') == "api/admin/refunds/export"
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        Assert.True(endpoint is not null, "GET api/admin/refunds/export is not mapped");

        var policies = endpoint!.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .OfType<string>()
            .ToList();

        Assert.Contains(Permissions.SupportTriage, policies);
        Assert.All(policies, policy => Assert.Contains(policy, Permissions.All));
        Assert.All(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            a => Assert.True(string.IsNullOrEmpty(a.Roles), "the export route is gated on role names"));
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
