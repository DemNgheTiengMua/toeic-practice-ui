using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Contracts;

namespace WebAPI.Tests;

/// <summary>
/// #206 / S-03: the audit's API surface.
/// <para>
/// <b>What this file asserts that a service test cannot:</b> the queue route carries no authored key,
/// and the reveal route is a POST rather than a GET. Both are properties of the wire contract. A GET
/// reveal would be prefetchable, bookmarkable and in browser history - three ways a reviewer sees the
/// key before solving, none of which any service-side precondition can prevent.
/// </para>
/// </summary>
public sealed class AuditEndpointTests : IClassFixture<AuditEndpointTests.Site>
{
    private readonly Site site;

    public AuditEndpointTests(Site site) => this.site = site;

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> exposing the key on the queue. The route
    /// table is read from the real host, so a future handler that added the property would not slip by.
    /// </summary>
    [Fact]
    public void Every_audit_route_is_gated_on_the_review_capability()
    {
        var auditRoutes = Routes()
            .Where(r => r.Route.StartsWith("/api/admin/audit", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(auditRoutes);
        Assert.All(auditRoutes, r => Assert.Contains(Permissions.ContentReview, r.Policies));
        // And none of them is authoring: an author who uploaded the batch cannot audit it.
        Assert.All(auditRoutes, r => Assert.DoesNotContain(Permissions.ContentAuthor, r.Policies));
    }

    /// <summary>
    /// The reveal is a POST. <b>Which guard could this pass while being wrong:</b> a GET, which is
    /// reachable by a browser prefetch, a bookmark and a back button - none of which the reviewer
    /// intended, and all of which would reveal a key before any solve.
    /// </summary>
    [Fact]
    public void The_key_reveal_is_a_post_and_never_a_get()
    {
        var reveals = Routes()
            .Where(r => r.Route.Contains("/reveal", StringComparison.Ordinal))
            .ToList();

        var reveal = Assert.Single(reveals);
        Assert.Equal("POST", reveal.Method, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The queue's DTO has no key-shaped member. <b>Which guard could this pass while being wrong:</b> a
    /// <c>KeyLabel</c> property added to <see cref="AuditSampleDto"/> later - blindness would then be a
    /// choice a page makes rather than a fact about the read.
    /// </summary>
    [Fact]
    public void The_queue_dto_carries_no_authored_key()
    {
        var members = typeof(AuditSampleDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("KeyLabel", members);
        Assert.DoesNotContain("Key", members);
        Assert.DoesNotContain("CorrectOption", members);

        // The reveal DTO is where it lives, and it is a different shape on a different route.
        Assert.Contains("KeyLabel", typeof(AuditKeyRevealDto).GetProperties().Select(p => p.Name));
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a route with no policy at all, which
    /// returns data to anyone holding a token rather than refusing. An anonymous request is the
    /// cheapest way to see the difference.
    /// <para>
    /// A signed-in learner is not tested by request: bearer validation re-checks the session against the
    /// database, so an unconfigured test host answers 401 before any policy runs, and that 401 would be
    /// an environment fact rather than the authorization decision. The decision is pinned above instead -
    /// every audit route requires <c>pool.content.review</c>, which a learner's role does not grant.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_anonymous_request_is_refused_the_queue()
    {
        using var client = site.CreateAnonymousClient();

        var response = await client.GetAsync("/api/admin/audit/batches/7");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private IEnumerable<(string Route, string? Method, IReadOnlyList<string> Policies)> Routes()
    {
        var endpoints = site.Services
            .GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>();

        foreach (var endpoint in endpoints)
        {
            var policies = endpoint.Metadata
                .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();
            var methods = endpoint.Metadata
                .OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                .SelectMany(m => m.HttpMethods)
                .ToList();

            yield return (
                "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                methods.Count == 0 ? null : methods[0],
                policies);
        }
    }

    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Jwt:Issuer", "webapi-tests");
            builder.UseSetting("Jwt:Audience", "webapi-tests");
        }

        /// <summary>Anonymous.</summary>
        public HttpClient CreateAnonymousClient() =>
            CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        /// <summary>A signed-in learner, whose role is not one of the five staff namespaces.</summary>
        public HttpClient CreateLearnerClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token());
            return client;
        }

        /// <summary>
        /// A token the test host accepts: minted with the same key the host validates with. The point of
        /// the test is the authorization decision, not the signature algorithm.
        /// </summary>
        private static string Token()
        {
            var factory = new JwtAccessTokenFactory(Options.Create(new JwtOptions
            {
                SigningKey = Convert.ToBase64String(new byte[32]),
                Issuer = "webapi-tests",
                Audience = "webapi-tests",
            }));

            return factory.Create(1, "learner@test.local", Roles.Learner, "Learner", "stamp").Token;
        }
    }
}
