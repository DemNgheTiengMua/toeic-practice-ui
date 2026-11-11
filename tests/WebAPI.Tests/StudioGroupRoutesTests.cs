using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #273: <b>the Studio's group write is a real route, and the two it replaces are not.</b>
/// <para>
/// Measured before this ticket: <c>PUT /admin/exams/1/studio/group/1</c> answered 404, because the MVC
/// controller forwarded to <c>api/admin/exams/{id}/groups/{groupId}</c> - a route #265 deleted with the
/// <c>QuestionGroups</c> table it wrote. This asserts both halves against the real <c>Program</c>: the
/// new pair is mapped on the authoring capability, and the old pair is still absent, so a re-add is
/// caught here rather than by a screen that silently stops saving.
/// </para>
/// </summary>
public sealed class StudioGroupRoutesTests : IClassFixture<StudioGroupRoutesTests.Site>
{
    private readonly Site site;

    public StudioGroupRoutesTests(Site site) => this.site = site;

    [Theory]
    [InlineData("PUT", "api/admin/exams/{examId:int}/studio/groups/{groupId:int}")]
    [InlineData("DELETE", "api/admin/exams/{examId:int}/studio/groups/{groupId:int}")]
    public void The_studio_group_routes_are_mapped_on_the_authoring_capability(string method, string route)
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

        Assert.Contains(Permissions.ContentAuthor, policies);
        Assert.All(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            a => Assert.True(string.IsNullOrEmpty(a.Roles), $"({method} /{route}) is gated on role names"));
    }

    /// <summary>
    /// The two routes #265 deleted, asserted absent rather than 404-at-request: a route that exists and
    /// 404s for another reason would pass a request-level check and still be a door onto a dead table.
    /// </summary>
    [Theory]
    [InlineData("PUT", "api/admin/exams/{examId:int}/groups/{groupId:int}")]
    [InlineData("DELETE", "api/admin/exams/{examId:int}/groups/{groupId:int}")]
    public void The_deleted_group_routes_are_still_absent(string method, string route)
    {
        var mapped = site.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/') == route
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .ToList();

        Assert.Empty(mapped);
    }

    /// <summary>
    /// The client must address the route that exists. This is the defect in one assertion: the two
    /// deleted methods of <see cref="IAdminExamsClient"/> are gone, and the pair that replaced them
    /// names <c>studio/groups</c>.
    /// </summary>
    [Fact]
    public void The_client_has_no_member_that_names_the_deleted_group_route()
    {
        var names = typeof(IAdminExamsClient)
            .GetMethods()
            .Select(m => m.Name)
            .Distinct()
            .ToList();

        Assert.DoesNotContain("UpdateAdminQuestionGroupAsync", names);
        Assert.DoesNotContain("DeleteAdminQuestionGroupAsync", names);
        Assert.Contains("UpdateExamStudioGroupAsync", names);
        Assert.Contains("DeleteExamStudioGroupAsync", names);
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
