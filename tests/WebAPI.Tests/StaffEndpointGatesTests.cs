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
/// #200 criteria 3 and 4 on the API side: every <c>/api/admin</c> route is gated by a capability and
/// none is gated by a role name. It reads the endpoint table the real <c>Program</c> built, so a
/// group left on <c>Roles.Admin</c> fails here rather than showing up as a 403 nobody reads.
/// </summary>
public sealed class StaffEndpointGatesTests : IClassFixture<StaffEndpointGatesTests.Site>
{
    private readonly Site site;

    public StaffEndpointGatesTests(Site site) => this.site = site;

    [Fact]
    public void Every_admin_route_names_capabilities_and_never_a_role()
    {
        var routes = Routes().ToList();
        var adminRoutes = routes.Where(r => r.Route.StartsWith("/api/admin", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(adminRoutes);

        var offenders = new List<string>();
        foreach (var (route, policies, roles) in adminRoutes)
        {
            foreach (var role in roles)
            {
                offenders.Add($"{route} is gated on role names '{role}'");
            }

            if (policies.Count == 0)
            {
                offenders.Add($"{route} names no capability policy");
            }

            foreach (var policy in policies.Where(p => !Permissions.All.Contains(p)))
            {
                offenders.Add($"{route} uses policy '{policy}', which is not a capability");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Criterion 4 for the whole API: no route anywhere is gated on the staff role name. Learner
    /// routes stay on <c>learner</c> on purpose — the five namespaces are staff authority, and there
    /// is no learner namespace to replace it with. The assertion is therefore specific: the
    /// <c>admin</c> role must appear in no route's metadata at all.
    /// </summary>
    [Fact]
    public void No_route_anywhere_is_gated_on_the_admin_role_name()
    {
        var roleGated = Routes()
            .SelectMany(r => r.Roles.Select(role => (r.Route, Role: role)))
            .ToList();

        var adminRole = roleGated
            .Where(r => r.Role.Contains("admin", StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{r.Route} requires role '{r.Role}'")
            .ToList();

        Assert.True(adminRole.Count == 0, string.Join(Environment.NewLine, adminRole));

        // The learner role is the only role name left, and it is not a staff gate.
        Assert.All(roleGated, r => Assert.Equal(Contracts.Roles.Learner, r.Role));
    }

    [Theory]
    [InlineData(Permissions.ContentAuthor)]
    [InlineData(Permissions.ContentReview)]
    [InlineData(Permissions.BlueprintDesign)]
    [InlineData(Permissions.SupportTriage)]
    [InlineData(Permissions.SystemAdmin)]
    public async Task The_api_registers_a_policy_for_each_of_the_five_namespaces(string capability)
    {
        var provider = site.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetPolicyAsync(capability);

        // A policy naming a code nobody registered would fail open ("no requirements") or closed
        // ("missing") depending on the handler, so it has to exist and carry a requirement.
        Assert.NotNull(policy);
        Assert.NotEmpty(policy.Requirements);
    }

    private IEnumerable<(string Route, List<string> Policies, List<string> Roles)> Routes() =>
        site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => (
                Route: "/" + endpoint.RoutePattern.RawText!.TrimStart('/'),
                Policies: endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(a => a.Policy).OfType<string>().ToList(),
                Roles: endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(a => a.Roles).OfType<string>().ToList()));

    /// <summary>The real WebAPI host. No request is made, so nothing connects to a database; the
    /// endpoint table and the policy provider are built at startup, which is all this reads.</summary>
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
