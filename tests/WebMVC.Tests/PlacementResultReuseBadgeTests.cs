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
/// #213 / story 55: "I want my result reused from a prior sitting to be badged as such, <b>so that I
/// always know how old each number is</b>."
///
/// This drives the REAL Razor view through the real host and asserts on the HTML the learner receives,
/// because the failure mode is a template that never renders the field. The wrong shape of test here is
/// the one the ticket's reviewer warned about: a source-grep for `ReusedFromPrior`, or an assertion that
/// the page contains the words "carried over". Both pass on a page that badges every row, and both pass
/// while the date is missing.
///
/// <b>The fixture is deliberately mixed:</b> one skill measured in this sitting and one carried over.
/// A page-level banner would satisfy "the badged words appear" and fail the second assertion below.
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class PlacementResultReuseBadgeTests
{
    private static readonly DateTimeOffset Taken = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services => services.AddSingleton<ICompanionApiClient>(Upstream.Create().Client));
        }

        public HttpClient CreateClientWithLearner()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var identity = new ClaimsIdentity(
                AuthClaims.For(2, "learner@test.local", Roles.Learner, "Learner"),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole);
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties,
                CookieAuthenticationDefaults.AuthenticationScheme);

            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            return client;
        }
    }

    [Fact]
    public async Task A_carried_skill_is_badged_with_its_original_date_and_a_measured_skill_is_not()
    {
        await using var site = new Site();
        using var client = site.CreateClientWithLearner();

        var response = await client.GetAsync("/placement/result/1");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} -> /placement/result/1");
        var html = await response.Content.ReadAsStringAsync();

        // Row by row, because the claim is per skill. The carried row says so AND names the ORIGINAL
        // measurement date (18 October), not the sitting's (8 November).
        var carried = Row(html, "Listening");
        Assert.Contains("Carried over from your sitting on 18 October 2026", carried, StringComparison.Ordinal);
        Assert.Contains("B1 (42/100)", carried, StringComparison.Ordinal);
        Assert.DoesNotContain("8 November 2026", carried, StringComparison.Ordinal);

        // The freshly measured skill in the SAME sitting: present, dated with the sitting, not badged.
        var fresh = Row(html, "Reading");
        Assert.Contains("Measured on 8 November 2026", fresh, StringComparison.Ordinal);
        Assert.Contains("A2 (10/100)", fresh, StringComparison.Ordinal);
        Assert.DoesNotContain("Carried over", fresh, StringComparison.Ordinal);
        Assert.DoesNotContain("18 October 2026", fresh, StringComparison.Ordinal);

        // Exactly one badge in the whole page, so a page-level banner on top of the rows fails here.
        Assert.Equal(1, CountOccurrences(html, ">Carried over</span>"));
    }

    /// <summary>
    /// The one table row whose header cell is <paramref name="skillName"/>. Splitting on the row boundary
    /// is what makes "this skill is badged" different from "this page mentions a badge somewhere", and
    /// keying on the row header keeps a skill mentioned elsewhere on the page out of the match.
    /// <para>
    /// #218 added a class to the row header so the signboard can style it, which is why the match allows
    /// attributes on the <c>th</c>. The claim is unchanged: the row's header cell must name the skill.
    /// </para>
    /// </summary>
    private static string Row(string html, string skillName)
    {
        var marker = $"<th scope=\"row\"";
        var at = -1;
        for (var i = html.IndexOf(marker, StringComparison.Ordinal); i >= 0;
             i = html.IndexOf(marker, i + marker.Length, StringComparison.Ordinal))
        {
            var close = html.IndexOf('>', i);
            if (close > 0 && html.IndexOf(skillName, close, StringComparison.Ordinal) is var nameAt
                && nameAt > 0 && nameAt - close < 120)
            {
                at = i;
                break;
            }
        }

        Assert.True(at >= 0, $"no row header names {skillName}");

        var start = html.LastIndexOf("<tr", at, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", at, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > at, $"the header for {skillName} is not inside a row");
        return html[start..(end + "</tr>".Length)];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Answers the calls the placement result page makes. Anything else throws rather than returning a
    /// plausible blank, so a new dependency shows up as a failing test instead of an empty section.
    /// </summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        public static Upstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(2, "learner@test.local", "Learner", Roles.Learner, false)),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Learner", null, "Asia/Ho_Chi_Minh")),
            nameof(ILearnerContextClient.GetLearnerContextAsync) => Task.FromResult(
                new LearnerContextDto(new CertificateOptionDto("DEMO", "Demo Certificate", "Unit"),
                    [new CertificateOptionDto("DEMO", "Demo Certificate", "Unit")], 8)),
            nameof(IPlacementClient.GetPlacementResultAsync) => Task.FromResult(Result()),
            // The result page pre-fetches an AI insight once grading settles; an unanswered insight is
            // logged and the page still renders, so the page must not depend on it.
            nameof(IAiInsightsClient.GetExamInsightAsync) => Task.FromResult<AiInsightDto?>(null),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        /// <summary>
        /// One sitting, two skills: Listening was CARRIED OVER from 18 October, Reading was measured on
        /// 8 November. The level and position are the stored ones, so the page is showing real numbers.
        /// </summary>
        private static PlacementResultViewDto Result() => new(
            1, 77, 1, "DEMO", "Placement", Taken, 30, 2, 150, "A2", "Elementary",
            [
                new CefrBandInfoDto("A2", "Elementary", 0, 200, true, true, false),
                new CefrBandInfoDto("B1", "Intermediate", 201, 500, false, false, false),
            ],
            [
                new PlacementSkillScoreInfoDto(2, "READ", "Reading", 1, 1, 100, "A2"),
            ],
            "Reading", null, false, 0, 0, 0, 1, true,
            [
                new PlacementSkillResultInfoDto(1, "LIST", "Listening", "Measured", "B1", 42,
                    new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero), ReusedFromPrior: true, null),
                new PlacementSkillResultInfoDto(2, "READ", "Reading", "Measured", "A2", 10,
                    Taken, ReusedFromPrior: false, null),
            ]);
    }
}
