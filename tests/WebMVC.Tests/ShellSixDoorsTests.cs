using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
/// #242 / SCR-SH-01: the shell's six doors, its non-clickable certificate placard, its exam-date
/// indicator and its offline banner.
/// <para>
/// <b>Why the real host and not a source grep.</b> Every criterion here is about what a learner
/// receives. A grep for "six items" passes on a rail that renders a seventh from a partial, and a
/// grep for `aria-disabled` passes on a door that is still an anchor with an href. These boot the
/// real Razor pipeline, render <c>/profile</c>, and assert on the markup.
/// </para>
/// <para>
/// <b>The input that would pass each guard while being wrong is stated above each test.</b>
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class ShellSixDoorsTests
{
    private static readonly ModalityDto[] FourSkills =
        [ModalityDto.Listening, ModalityDto.Reading, ModalityDto.Speaking, ModalityDto.Writing];

    private static readonly ModalityDto[] TwoSkills =
        [ModalityDto.Listening, ModalityDto.Reading];

    private static string Render(LearnerContextDto shell, string query = "")
    {
        using var site = new Site(shell);
        using var client = site.CreateClientWithLearner();
        var response = client.GetAsync($"/profile{query}").GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static string Nav(string html) =>
        Regex.Match(html, @"<nav class=""nav-list""[^>]*>(?<body>[\s\S]*?)</nav>").Groups["body"].Value;

    /// <summary>The doors, whichever shape they take: an anchor, or the explained resting sign.</summary>
    private static int DoorCount(string html) =>
        Regex.Matches(Nav(html), @"class=""nav-list__item(?:\s|"")").Count;

    /// <summary>Doors that are anchors with an href, in rail order.</summary>
    private static IReadOnlyList<string> OpenDoors(string html) =>
        Regex.Matches(Nav(html), @"<a class=""[^""]*nav-list__item[^""]*""\s+href=""(?<href>[^""]+)""")
            .Select(m => m.Groups["href"].Value)
            .ToList();

    /// <summary>The doors rendered as the non-interactive state, with the reason they read.</summary>
    private static IReadOnlyList<string> InactiveDoors(string html) =>
        Regex.Matches(Nav(html), @"<span class=""[^""]*nav-list__item--inactive[^""]*""[\s\S]*?</span>\s*</span>")
            .Select(m => m.Value)
            .ToList();

    // ------------------------------------------------------------------------------------------
    // exactly six doors
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> five anchors plus the missing
    /// <c>/learning-path</c> door - the exact state measured on <c>7d5f0b4</c>, where
    /// <c>LearningPathController</c> rendered and nothing linked to it. The count is six and the
    /// hrefs are named, so a rail that swapped one door for another still fails.
    /// </summary>
    [Fact]
    public void A_four_skill_certificate_gets_six_open_doors_naming_path_exams_practice_writing_speaking_home()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
            [new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading")],
            8,
            FourSkills));

        Assert.Equal(6, DoorCount(html));
        Assert.Equal(
            ["/learning-path", "/exams", "/practice", "/writing", "/speaking", "/dashboard"],
            OpenDoors(html));
        Assert.Empty(InactiveDoors(html));
    }

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> six doors for a two-skill
    /// certificate achieved by hiding two of them - the criterion is six, two of them non-clickable,
    /// so a rail that renders four open doors passes the "no dead links" reading and fails here.
    /// </summary>
    [Fact]
    public void A_two_skill_certificate_keeps_six_doors_and_shows_two_as_inactive()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC_CAT", "TOEIC Multi-Stage Adaptive CAT"),
            [new CertificateOptionDto("TOEIC_CAT", "TOEIC Multi-Stage Adaptive CAT")],
            8,
            TwoSkills));

        Assert.Equal(6, DoorCount(html));

        // The four learning doors this certificate examines stay open...
        Assert.Equal(["/learning-path", "/exams", "/practice", "/dashboard"], OpenDoors(html));
        // ...and the two it does not are still present, as a state.
        Assert.Equal(2, InactiveDoors(html).Count);

        // The doors are not removed: the labels are still on the rail, in place.
        var nav = Nav(html);
        Assert.Contains("Writing", nav);
        Assert.Contains("Speaking", nav);
    }

    /// <summary>
    /// The trap the ticket names: a disabled door rendered as an anchor with
    /// <c>pointer-events: none</c> stays a link to the keyboard and to a screen reader.
    /// <b>The input that passes a weaker guard while being wrong:</b> an anchor with
    /// <c>aria-disabled="true"</c>, which a tab stop still reaches. This asserts there is no anchor
    /// for an inactive door at all, and that the element carries the reason.
    /// </summary>
    [Fact]
    public void An_inactive_door_is_not_a_link_and_states_why_it_cannot_open()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC_CAT", "TOEIC Multi-Stage Adaptive CAT"),
            [new CertificateOptionDto("TOEIC_CAT", "TOEIC Multi-Stage Adaptive CAT")],
            8,
            TwoSkills));

        var nav = Nav(html);

        // Not an anchor, not a button: nothing focusable carries the door.
        Assert.DoesNotContain("href=\"/writing\"", nav);
        Assert.DoesNotContain("href=\"/speaking\"", nav);
        Assert.DoesNotContain("<button", nav);

        foreach (var door in InactiveDoors(html))
        {
            // Non-interactive: nothing a keyboard or assistive technology can operate.
            Assert.DoesNotContain("<a ", door);
            Assert.DoesNotContain("<button", door);
            Assert.DoesNotContain("tabindex", door);
            Assert.DoesNotContain("role=\"link\"", door);
            Assert.DoesNotContain("href=", door);
            // The state is not a mystery: the reason travels with the door.
            Assert.Contains("Not examined by this certificate", door);
        }
    }

    /// <summary>
    /// A certificate whose skill list did not arrive must not lock a learner out of a screen they
    /// own. <b>The input that passes the "two inactive doors" guard while being wrong:</b> treating
    /// a missing list as "examines nothing", which disables every door on a projection failure.
    /// </summary>
    [Fact]
    public void A_certificate_with_no_reported_skill_list_leaves_every_door_open()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8));

        Assert.Equal(6, DoorCount(html));
        Assert.Empty(InactiveDoors(html));
    }

    // ------------------------------------------------------------------------------------------
    // the placard is not a control
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> a placard that is a
    /// <c>&lt;details&gt;</c> disclosure of submit buttons - which is exactly what shipped. It is not
    /// an <c>&lt;a href&gt;</c>, so a narrow "not a link" assertion passes it. This asserts the
    /// element is a plain span, that no switcher form or disclosure exists in the shell at all, and
    /// that the one place the choice changes is named.
    /// </summary>
    [Fact]
    public void The_certificate_placard_is_not_clickable_and_points_at_profile_settings()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
            [new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading")],
            8,
            FourSkills));

        // The placard region: the span itself, then the one link that names where the choice moves.
        var placard = Regex.Match(
            html,
            @"<span class=""cert-placard""[\s\S]*?href=""(?<href>[^""]+)""");
        Assert.True(placard.Success, "the certificate placard is not followed by a link");
        Assert.Equal("/profile", placard.Groups["href"].Value);

        // The placard element is a plain span: no anchor, no disclosure, no form, no button.
        var placardElement = Regex.Match(html, @"<span class=""cert-placard""[\s\S]*?</span>\s*</span>").Value;
        Assert.NotEmpty(placardElement);
        Assert.DoesNotContain("<a ", placardElement);
        Assert.DoesNotContain("<details", placardElement);
        Assert.DoesNotContain("<form", placardElement);
        Assert.DoesNotContain("<button", placardElement);
        Assert.DoesNotContain("aria-haspopup", placardElement);

        // And the old one-click switcher is gone from the shell entirely.
        Assert.DoesNotContain("cert-switcher", html);
        Assert.Contains("TOEIC", placardElement);
    }

    /// <summary>
    /// LRN-09's other half: the switch exists somewhere, and only in profile settings.
    /// <b>The input that passes the previous test while being wrong:</b> deleting the switcher
    /// entirely, leaving no way to change certificate. This asserts the form is on the profile page.
    /// </summary>
    [Fact]
    public void Profile_settings_is_the_one_place_the_certificate_changes()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
            [new CertificateOptionDto("TOEIC", "TOEIC Listening & Reading"),
             new CertificateOptionDto("IELTS", "IELTS Academic & General")],
            8,
            FourSkills));

        Assert.Contains("action=\"/learner/active-certificate\"", html);
        Assert.Contains("name=\"code\"", html);
        Assert.Contains("value=\"IELTS\"", html);
    }

    // ------------------------------------------------------------------------------------------
    // the exam-date indicator
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes this guard while being wrong:</b> rendering the indicator
    /// unconditionally, so a learner with no goal sees a blank field or "1 January 1970". This
    /// asserts the whole indicator is absent for a null date.
    /// </summary>
    [Fact]
    public void A_learner_with_no_exam_date_renders_no_exam_date_indicator()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills,
            ExamDate: null));

        Assert.DoesNotContain("Exam date", html);
        Assert.DoesNotContain("1970", html);
        Assert.DoesNotContain("0001", html);
    }

    /// <summary>
    /// A date outside the window is a date. <b>The input that passes the countdown test while being
    /// wrong:</b> "always a countdown", which LRN-15 explicitly rules out ("365 days left" is noise).
    /// </summary>
    [Fact]
    public void An_exam_date_far_away_is_shown_as_a_date_and_not_a_countdown()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(120);

        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills,
            date));

        Assert.Contains("Exam date", html);
        Assert.Contains($"datetime=\"{date:yyyy-MM-dd}\"", html);
        Assert.Contains(date.ToString("d MMM yyyy"), html);
        Assert.DoesNotContain("days to exam", html);
        Assert.DoesNotContain("context-bar__value--countdown", html);
    }

    /// <summary>
    /// LRN-15: inside fourteen days the same figure turns gold and counts down.
    /// <b>The input that passes a "countdown exists" guard while being wrong:</b> a countdown that
    /// never turns gold, which is the pressure the decision exists for.
    /// </summary>
    [Fact]
    public void An_exam_date_inside_fourteen_days_becomes_a_gold_countdown()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills,
            date));

        Assert.Contains("3 days to exam", html);
        Assert.Contains("context-bar__value--countdown", html);
    }

    [Fact]
    public void Exam_day_reads_as_today_rather_than_zero_days()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills,
            date));

        Assert.Contains("Exam today", html);
        Assert.DoesNotContain("0 days to exam", html);
    }

    // ------------------------------------------------------------------------------------------
    // the offline banner
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes a "banner exists" guard while being wrong:</b> a banner that only
    /// appears while <c>navigator.onLine</c> is false, which no server-rendered test can ever see.
    /// This asserts a deterministic trigger: <c>?offline=1</c> raises the same attribute the
    /// stylesheet paints from.
    /// </summary>
    [Fact]
    public void The_offline_banner_is_in_the_shell_and_has_a_deterministic_trigger()
    {
        var online = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills));

        // Present on every page, painted only when the condition is stated.
        Assert.Contains("offline-banner", online);
        Assert.DoesNotContain("data-offline=\"true\"", online);

        var offline = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills), "?offline=1");

        Assert.Contains("data-offline=\"true\"", offline);
    }

    /// <summary>
    /// U16: offline outside a sitting is banner-only, and it says so rather than pretending to save.
    /// <b>The input that passes the attribute test while being wrong:</b> an empty banner, which
    /// tells the learner nothing. This asserts the words and that it is a live region.
    /// </summary>
    [Fact]
    public void The_offline_banner_states_that_nothing_is_being_saved()
    {
        var html = Render(new LearnerContextDto(
            new CertificateOptionDto("TOEIC", "TOEIC"),
            [new CertificateOptionDto("TOEIC", "TOEIC")],
            8,
            FourSkills));

        var banner = Regex.Match(html, @"<div class=""offline-banner""[^>]*>(?<body>[\s\S]*?)</div>").Value;

        Assert.NotEmpty(banner);
        Assert.Contains("role=\"alert\"", banner);
        Assert.Contains("OFFLINE", banner);
        Assert.Contains("nothing", banner, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------------------------
    // the fixture host
    // ------------------------------------------------------------------------------------------

    /// <summary>The real WebMVC host with only the upstream API replaced. Everything else - cookie
    /// scheme, middleware order, Razor - is production code.</summary>
    public sealed class Site(LearnerContextDto shell) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
            {
                var upstream = ShellUpstream.Create();
                upstream.Shell = shell;
                services.AddSingleton<ICompanionApiClient>(upstream.Client);
            });
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

    private class ShellUpstream : DispatchProxy
    {
        public LearnerContextDto Shell { get; set; } = null!;

        public ICompanionApiClient Client { get; private set; } = null!;

        public static ShellUpstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, ShellUpstream>();
            var stub = (ShellUpstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAuthClient.GetMeAsync) => Task.FromResult(
                new AuthUserDto(2, "learner@test.local", "Learner", Roles.Learner, true)),
            nameof(IProfileClient.GetProfileAsync) => Task.FromResult(
                new ProfileDto("Learner", null, "Asia/Ho_Chi_Minh")),
            nameof(ILearnerContextClient.GetLearnerContextAsync) => Task.FromResult(Shell),
            _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
        };
    }
}
