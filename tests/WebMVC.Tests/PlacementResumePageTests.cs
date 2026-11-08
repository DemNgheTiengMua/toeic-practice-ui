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
/// #216 / PLC-14: "I want to return after closing the tab, see which skills are done, which stage I am
/// on, and how many answers are synced, and choose to resume or abandon."
/// <para>
/// This drives the REAL Razor view through the real host and asserts on the HTML the learner receives,
/// because the failure mode is a template that renders a blank where the fact should be. The wrong
/// shape of test is a source-grep for the field names: that passes on a page that never prints the
/// value.
/// </para>
/// <para>
/// <b>The input that would pass a weaker test:</b> a page that prints only "in progress". It satisfies
/// "there is a resume screen" while telling the learner none of the five facts, so every one of them is
/// asserted separately and in the row that owns it.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class PlacementResumePageTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 6, 9, 15, 0, TimeSpan.Zero);

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
    public async Task The_resume_page_names_the_start_date_the_done_skills_the_stage_the_synced_count_and_both_choices()
    {
        await using var site = new Site();
        using var client = site.CreateClientWithLearner();

        var response = await client.GetAsync("/placement/take/5/resume");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // 1. The start date, formatted in the learner's zone (th timezone here is the default).
        Assert.Contains("6 Nov 2026", html, StringComparison.Ordinal);

        // 2. Which skills are done - as WORDS, per skill, not a single page-level state.
        Assert.Contains("Done", Row(html, "Listening"), StringComparison.Ordinal);
        Assert.Contains("In progress", Row(html, "Reading"), StringComparison.Ordinal);
        Assert.Contains("Not started", Row(html, "Speaking"), StringComparison.Ordinal);

        // 3. The active stage, named and numbered.
        Assert.Contains("Stage 2 of 3", html, StringComparison.Ordinal);
        Assert.Contains("Reading", html, StringComparison.Ordinal);

        // 4. How many answers are SYNCED - the server's count, which is the only one it can know.
        Assert.Contains("7 of 24 answers saved", html, StringComparison.Ordinal);

        // 5. Both choices, and abandon is a POST carrying an anti-forgery token, never a link.
        Assert.Contains("/placement/take/5", html, StringComparison.Ordinal);
        Assert.Contains("Resume at question", html, StringComparison.Ordinal);
        Assert.Contains("Abandon this test", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/placement/take/5/abandon\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);

        // The page never renders a question, so it cannot advance the learner by being opened.
        Assert.DoesNotContain("js-option-radio", html, StringComparison.Ordinal);
    }

    /// <summary>The one table row whose header cell is <paramref name="skillName"/>.</summary>
    private static string Row(string html, string skillName)
    {
        var marker = $"<th scope=\"row\">{skillName}</th>";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no row header names {skillName}");

        var start = html.LastIndexOf("<tr", at, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", at, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > at, $"the header for {skillName} is not inside a row");
        return html[start..(end + "</tr>".Length)];
    }

    /// <summary>
    /// Answers the calls the resume page makes. Anything else throws rather than returning a plausible
    /// blank, so a new dependency shows up as a failing test instead of an empty section.
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
            nameof(IPlacementClient.GetPlacementResumeAsync) => Task.FromResult(Resume()),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };

        /// <summary>
        /// A sitting two stages in: Listening is closed with nothing answered (done is about closure),
        /// Reading is open with 7 of 12 answered, Speaking has not started.
        /// </summary>
        private static PlacementResumeViewDto Resume() => new(
            5, 77, "TOEIC Placement Test", "DEMO", Started, Started.AddHours(40),
            [
                new PlacementResumeSkillDto(1, "LIST", "Listening", IsDone: true, IsActive: false, AnsweredCount: 0, TotalQuestions: 8),
                new PlacementResumeSkillDto(2, "READ", "Reading", IsDone: false, IsActive: true, AnsweredCount: 7, TotalQuestions: 12),
                new PlacementResumeSkillDto(3, "SPEAK", "Speaking", IsDone: false, IsActive: false, AnsweredCount: 0, TotalQuestions: 4),
            ],
            ActiveSkillId: 2,
            ActiveSkillName: "Reading",
            ActiveStage: 2,
            TotalStages: 3,
            SyncedAnswerCount: 7,
            TotalQuestions: 24,
            LastQuestionId: 42);
    }
}
