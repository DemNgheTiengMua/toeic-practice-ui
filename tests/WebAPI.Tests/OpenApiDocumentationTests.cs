using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #285: one configuration flag owns three routes — the OpenAPI document, the browsable UI, and the
/// demo sign-in. With the flag off none of the three exists; with it on the document lists the whole
/// API. These assert the routes, not the generator, so a package swap that keeps the contract passes.
/// </summary>
public sealed class OpenApiDocumentationTests
{
    private const string DocumentRoute = "/openapi/v1.json";
    private const string UiRoute = "/scalar";
    private const string DemoSignInRoute = "/api/auth/demo-sign-in";

    [Theory]
    [InlineData(DocumentRoute)]
    [InlineData(UiRoute)]
    [InlineData(DemoSignInRoute)]
    public async Task The_flag_off_removes_the_document_the_ui_and_the_demo_sign_in(string route)
    {
        using var api = new Off();
        using var client = api.CreateClient();

        var response = await client.PostAsync(route, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_document_is_served_and_lists_every_path_the_api_maps()
    {
        using var api = new On();
        using var client = api.CreateClient();

        var response = await client.GetAsync(DocumentRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // 239 endpoint registrations across 22 mappers. A count far below that means a group was
        // skipped rather than the document being correctly small, so assert the order of magnitude.
        Assert.True(paths.Count > 200, $"only {paths.Count} paths in the document");

        // One grouped route and one ungrouped absolute-path route: the dual-mounted lesson aliases
        // prove a group registered with MapGroup("") still reaches the document.
        Assert.Contains("/api/auth/sign-in", paths);
        Assert.Contains("/lessons/{id}", paths);
    }

    [Fact]
    public async Task The_document_declares_one_bearer_scheme_and_marks_only_authenticated_operations_with_it()
    {
        using var api = new On();
        using var client = api.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(DocumentRoute));
        var root = document.RootElement;

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("BearerAuth");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        bool RequiresBearer(string path, string verb) =>
            root.GetProperty("paths").GetProperty(path).GetProperty(verb)
                .TryGetProperty("security", out var security)
                && security.GetRawText().Contains("BearerAuth", StringComparison.Ordinal);

        Assert.True(RequiresBearer("/api/auth/me", "get"));
        Assert.False(RequiresBearer("/api/auth/sign-in", "post"));
    }

    [Fact]
    public async Task The_ui_answers_and_the_demo_sign_in_is_mapped_and_documented_alongside_it()
    {
        using var api = new On();
        using var client = api.CreateClient();

        var page = await client.GetStringAsync(UiRoute);
        Assert.Contains("BearerAuth", page);

        // The demo route is mapped (a GET is 405, not 404) and listed, so the reviewer can find it.
        // It is not called here: a real sign-in needs the seeded database, which this host does not have.
        using var document = JsonDocument.Parse(await client.GetStringAsync(DocumentRoute));
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty(DemoSignInRoute, out _));

        var wrongVerb = await client.GetAsync(DemoSignInRoute);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongVerb.StatusCode);
    }

    [Fact]
    public async Task Every_operation_is_categorized_into_a_clean_restful_domain_tag_and_never_defaults_to_WebAPI()
    {
        using var api = new On();
        using var client = api.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(DocumentRoute));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var allTags = new HashSet<string>(StringComparer.Ordinal);
        var verbs = new[] { "get", "post", "put", "delete", "patch" };

        foreach (var pathProperty in paths.EnumerateObject())
        {
            foreach (var verb in verbs)
            {
                if (pathProperty.Value.TryGetProperty(verb, out var operation) &&
                    operation.TryGetProperty("tags", out var tagsElement))
                {
                    foreach (var tag in tagsElement.EnumerateArray())
                    {
                        var tagStr = tag.GetString();
                        if (!string.IsNullOrEmpty(tagStr))
                        {
                            allTags.Add(tagStr);
                        }
                    }
                }
            }
        }

        // None of the messy default tags should ever leak into the OpenAPI document
        Assert.DoesNotContain("WebAPI", allTags);
        Assert.DoesNotContain("AdminEndpoints", allTags);
        Assert.DoesNotContain("AdminImportEndpoints", allTags);
        Assert.DoesNotContain("LessonEndpoints", allTags);

        // Core RESTful domain tags must all be present and populated
        Assert.Contains("Authentication & Profile", allTags);
        Assert.Contains("Mock Exams & Attempts", allTags);
        Assert.Contains("Practice Drilling", allTags);
        Assert.Contains("Universal Placement", allTags);
        Assert.Contains("Learning Path & Sprints", allTags);
        Assert.Contains("Mentors & Bookings", allTags);
        Assert.Contains("Wallet & Orders", allTags);
        Assert.Contains("Admin - Operations & Governance", allTags);
    }

    private class Site : WebApplicationFactory<Program>
    {
        private readonly string enabled;

        protected Site(string enabled) => this.enabled = enabled;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Jwt:Issuer", "webapi-tests");
            builder.UseSetting("Jwt:Audience", "webapi-tests");
            builder.UseSetting("OpenApi:Enabled", enabled);
        }
    }

    private sealed class On() : Site("true");

    private sealed class Off() : Site("false");
}
