using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WebAPI.Tests;

public sealed class MediaStaticAndAliasRouteTests
{
    [Theory]
    [InlineData("/audio/toeic/placement-p1.mp3", "audio/mpeg")]
    [InlineData("/img/part1-sample.svg", "image/svg+xml")]
    public async Task Static_seed_media_is_served_directly_by_webapi(string path, string expectedContentType)
    {
        using var factory = new TestSite();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/media/exams/1/0123456789abcdef0123456789abcdef.mp3")]
    [InlineData("/media/imports/1/0123456789abcdef0123456789abcdef.mp3")]
    public async Task Media_alias_routes_are_mapped_and_require_auth(string path)
    {
        using var factory = new TestSite();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class TestSite : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Jwt:Issuer", "webapi-tests");
            builder.UseSetting("Jwt:Audience", "webapi-tests");
        }
    }
}
