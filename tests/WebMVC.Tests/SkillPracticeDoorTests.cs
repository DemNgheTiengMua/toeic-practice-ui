using System.Net;
using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WebMVC.Tests;

/// <summary>
/// #159: Writing and Speaking are the two sidebar entries the ticket adds because the features had
/// no door. These render the pages that door opens through the real host, because the defect they
/// catch is a layout one: <c>Views/Writing/Index.cshtml</c> and <c>Views/Speaking/Index.cshtml</c>
/// render a partial whose own <c>@{ Layout = "_LearnerLayout"; }</c> nests a whole page inside
/// <c>_Layout</c>. Reading the markup would not have shown it — the pages were green in every test
/// and had a duplicate top bar in the browser.
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class SkillPracticeDoorTests : IClassFixture<SkillPracticeDoorTests.Site>
{
    private static readonly string[] Doors = ["/writing", "/speaking"];

    private readonly Site site;

    public SkillPracticeDoorTests(Site site) => this.site = site;

    [Theory]
    [InlineData("/writing")]
    [InlineData("/speaking")]
    public async Task The_door_renders_one_page_not_a_page_inside_a_page(string path)
    {
        using var client = site.CreateClientWithLearner();
        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode) Assert.Fail($"{(int)response.StatusCode} -> {response.Headers.Location}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);;

        var body = await response.Content.ReadAsStringAsync();

        // A nested layout renders _Layout's document AND the learner layout's inside it: two
        // <html> elements, two top bars, two sign-out buttons.
        Assert.Equal(1, CountOccurrences(body, "<html"));
        Assert.Equal(1, CountOccurrences(body, "<!DOCTYPE html"));
        Assert.Equal(1, CountOccurrences(body, "class=\"app-topbar\""));
        Assert.Equal(1, CountOccurrences(body, "Sign out"));

        // ...and the door must render the LEARNER shell, not the public marketing one. Both layouts
        // have one <html>, one topbar and one "Sign out", so the assertions above pass for either -
        // which is how /writing and /speaking shipped without the five-item sidebar. The sidebar and
        // the credit badge exist only in _LearnerLayout.
        Assert.Contains("app-sidebar", body);
        Assert.Contains("cert-switcher", body);
    }

    [Theory]
    [InlineData("/writing", "Writing practice")]
    [InlineData("/speaking", "Speaking practice")]
    public async Task The_door_carries_its_own_title(string path, string expectedTitle)
    {
        using var client = site.CreateClientWithLearner();
        var body = await client.GetStringAsync(path);

        // The partial sets ViewData["Title"], but a layout renders <head> before RenderBody, so a
        // layout chosen inside the body leaves the title empty.
        Assert.Contains($"<title>{expectedTitle}", body);
        Assert.DoesNotContain("<title> — extra efficient", body);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The real WebMVC host with the writing/speaking catalog replaced, so nothing leaves the
    /// process. The catalog is the only upstream the door needs.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISkillPracticeClient>(new SkillPracticeStub
                {
                    List = new SkillPracticeExamListDto(
                        new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
                        3,
                        [new SkillPracticeExamDto(1, "Writing task 1", "Write an email.", 30, 1, null, null, null)]),
                });

                // The learner layout's cookie re-validation and the time-zone lookup both call the
                // API before the page action runs; without them the request 302s to sign-in and the
                // layout assertions would never be reached.
                services.AddSingleton<ICompanionApiClient>(DoorUpstream.Create().Client);
            });
        }

        public HttpClient CreateClientWithLearner()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            // The identity must declare the compact claim types as its name/role claim types, or
            // RequireRole falls back to the long XML-namespace defaults and /writing 403s.
            var identity = new ClaimsIdentity(
                AuthClaims.For(2, "learner@test.local", Roles.Learner, "Learner"),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole);
            var principal = new ClaimsPrincipal(identity);
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);

            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return client;
        }
    }
}

/// <summary>The two upstream calls the learner layout makes before any page action runs.</summary>
public class DoorUpstream : DispatchProxy
{
    public ICompanionApiClient Client { get; private set; } = null!;

    public static DoorUpstream Create()
    {
        var proxy = DispatchProxy.Create<ICompanionApiClient, DoorUpstream>();
        var stub = (DoorUpstream)(object)proxy;
        stub.Client = proxy;
        return stub;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(2, "learner@test.local", "Learner", Roles.Learner, false)),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Learner", null, "Asia/Ho_Chi_Minh")),
            nameof(ILearnerContextClient.GetLearnerContextAsync) => Task.FromResult(
                new LearnerContextDto(
                    new CertificateOptionDto("TOEIC", "TOEIC"),
                    [new CertificateOptionDto("TOEIC", "TOEIC")],
                    8)),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
}
