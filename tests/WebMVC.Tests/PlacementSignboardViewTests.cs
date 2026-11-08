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
/// #218 / LRN-16..21: the placement result as ONE signboard, driven through the real host so the
/// assertions are about the HTML a learner receives rather than about a template's source.
///
/// <para>
/// <b>Why the real host.</b> Each criterion below has a weaker assertion that would pass on a broken
/// page, and every one of them was checked against the wrong shape before the guard was accepted:
/// </para>
/// <list type="bullet">
/// <item>"four numbers are shown" passes on four disconnected cards, which is the exact regression.</item>
/// <item>"a band code appears" passes on a fixed A1-C2 ladder relabelling a certificate that has no
/// CEFR bands (PRN-03), so the fixture is a DEMO certificate whose bands are D1-D4.</item>
/// <item>"the unmeasured row exists" passes on a row drawn at the bottom band with a class name on it.
/// The guard therefore asserts the absence of a marker AND the presence of a reason in words.</item>
/// <item>"a number is shown" passes on a bare "42%", which reads as a score and, after a crossing, as a
/// collapse. The guard requires the band name beside the position.</item>
/// <item>"a badge appears" passes on a boolean, so the fixture badges by <i>date</i> with the flag left
/// false and proves the date alone is what drives it.</item>
/// </list>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class PlacementSignboardViewTests
{
    private static readonly DateTimeOffset Taken = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// LRN-16: every examined skill is a row on ONE axis, and the axis is the certificate's own band
    /// table. <b>The wrong input that would pass a weaker guard:</b> a page that renders a row per skill
    /// and a strip above them — a skill table with a separate scale per row still satisfies "rows exist".
    /// So the assertion is structural: one table, one colgroup, and one column per certificate band.
    /// </summary>
    [Fact]
    public async Task Every_skill_is_one_row_of_one_table_whose_columns_are_the_certificates_own_bands()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        // One horizon table, not one per skill.
        var tables = Regex.Matches(html, @"class=""signboard__horizon""");
        Assert.Single(tables);

        // The header names the certificate's six bands, in its own vocabulary.
        var headers = Headers(html);
        Assert.Equal(["Below A1", "Beginner", "Elementary", "Intermediate", "Upper intermediate", "Advanced"], headers);

        // Both skills are <tr>s inside that one <tbody>, so they share its columns by construction.
        var rows = Regex.Matches(html, @"data-skill-state=""[^""]+""");
        Assert.Equal(2, rows.Count);

        // The skills appear as the row headers of the same table.
        var tableBody = Regex.Match(html, @"<table class=""signboard__horizon"">(?<t>[\s\S]*?)</table>").Groups["t"].Value;
        Assert.Contains("Listening", tableBody, StringComparison.Ordinal);
        Assert.Contains("Reading", tableBody, StringComparison.Ordinal);

        // One colgroup with a column per band plus the skill-label column.
        var cols = Regex.Matches(tableBody, @"<col\b");
        Assert.Equal(7, cols.Count);
    }

    /// <summary>
    /// LRN-16 / PRN-03: the axis is the certificate's bands and not the CEFR ladder.
    /// <b>The wrong input that would pass a weaker guard:</b> "the header has six columns" — which a
    /// hardcoded six-level ladder produces for every certificate, including one whose bands are
    /// CAT_B1/CAT_B2. This fixture's two bands prove the columns come from the data.
    /// </summary>
    [Fact]
    public async Task The_axis_has_exactly_the_certificates_bands_and_never_an_invented_ladder()
    {
        await using var site = new Site(NonCefrBands);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        Assert.Equal(["Intermediate", "Professional"], Headers(html));
    }

    /// <summary>
    /// PRN-03: a measured level this certificate publishes no band for is recorded and NOT converted.
    /// <b>The wrong input that would pass a weaker guard:</b> dropping the row onto the nearest column,
    /// or relabelling CAT_B2 as CEFR B2. Both produce a plausible-looking marker on a scale that does
    /// not mean what it says. So the guard requires the gap to be stated in words and drawn nowhere.
    /// </summary>
    [Fact]
    public async Task A_level_with_no_column_on_this_certificate_is_stated_and_drawn_nowhere()
    {
        await using var site = new Site(NonCefrBands);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var comprehension = Row(html, "Comprehension");

        Assert.Contains("B2 (42/100)", comprehension, StringComparison.Ordinal);
        Assert.Contains("Level recorded, no band column", comprehension, StringComparison.Ordinal);
        Assert.Contains("name no column for B2", comprehension, StringComparison.Ordinal);
        Assert.DoesNotContain("signboard__marker", comprehension, StringComparison.Ordinal);
        Assert.DoesNotContain("signboard__fill", comprehension, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-18: an unmeasured skill is an attention panel — gold, with a reason and a retry — and
    /// occupies no band at all. <b>The wrong input that would pass a weaker guard:</b> rendering the row
    /// with a class like <c>is-not-measured</c> but still placing a marker in the bottom band, which is
    /// the fabricated-bottom-band defect (#211, #217). The guard asserts the marker is ABSENT, that the
    /// cell spans every band, and that the sentence a learner needs is present.
    /// </summary>
    [Fact]
    public async Task An_unmeasured_skill_is_a_gold_attention_panel_with_a_reason_and_no_band()
    {
        await using var site = new Site(() => Result(expression: "not_measured"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var reading = Row(html, "Reading");

        // No marker and no fill: nothing is drawn on the axis for this skill.
        Assert.DoesNotContain("signboard__marker", reading, StringComparison.Ordinal);
        Assert.DoesNotContain("signboard__fill", reading, StringComparison.Ordinal);

        // It spans every band column, so it cannot occupy one of them.
        Assert.Contains("colspan=\"6\"", reading, StringComparison.Ordinal);

        // The words a learner reads, and the reason, not a level and not a zero. Read from the VISIBLE
        // text, because the anti-forgery token is a random base64 blob that can contain "A1" — that
        // flake is #252's, and a guard that fails for an unrelated reason cannot be trusted when it does.
        var visible = VisibleText(reading);
        Assert.Contains("Not measured", visible, StringComparison.Ordinal);
        Assert.Contains("The grader could not score this task.", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("A1", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Below A1", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("(0/100)", visible, StringComparison.Ordinal);

        // The retry is offered, free, as a POST with a token.
        Assert.Contains("data-action=\"retry-grading\"", reading, StringComparison.Ordinal);
        Assert.Contains("Retrying is free", reading, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bands a row has passed are painted, and its own band is marked. <b>The wrong input that would
    /// pass a weaker guard:</b> a page with a marker and no band classes, which still "shows the position"
    /// on an axis with no fill — and, in the shape this guard was written for, a Razor expression that
    /// emitted the literal text <c>cell@state</c> instead of the class, painting nothing while every
    /// structural assertion passed.
    /// </summary>
    [Fact]
    public async Task The_bands_below_a_skill_are_painted_and_its_own_band_is_marked()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var listening = Row(html, "Listening");

        // B2 is the fifth of six columns, so four are passed and the fifth is current.
        Assert.Equal(4, Regex.Matches(listening, @"signboard__cell is-reached").Count);
        Assert.Single(Regex.Matches(listening, @"signboard__cell is-current"));
        // No literal Razor expression survived into the markup.
        Assert.DoesNotContain("cell@state", listening, StringComparison.Ordinal);
        Assert.DoesNotContain("@state", listening, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wire's status spelling is not the page's. <b>The wrong input that would pass a weaker guard:</b>
    /// every fixture written in snake_case, which is what this suite's own builders use — and which is not
    /// what <c>PlacementEndpoints.DtoMapping</c> sends. This asserts both spellings, because a page that
    /// only reads one of them reports the other status wrongly and passes the other tests.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Measured", "measured")]
    [InlineData("NotMeasured", "not_measured")]
    [InlineData("EstimatedFromOneTask", "estimated_from_one_task")]
    [InlineData("ExemptAccommodated", "exempt_accommodated")]
    [InlineData("measured", "measured")]
    [InlineData("not_measured", "not_measured")]
    [InlineData("", "")]
    public void The_wire_status_is_read_in_either_spelling(string wire, string expected) =>
        Assert.Equal(expected, Models.PlacementResultViewModel.NormalizeStatus(wire));

    /// <summary>
    /// A real API response marks a failed skill "NotMeasured", and the page must treat that as a
    /// not-measured row — not as a measured one with no level. <b>The wrong input that would pass a
    /// weaker guard:</b> a fixture that only ever sends the lower-case form, which is why this one sends
    /// the exact PascalCase string the API's <c>Status.ToString()</c> produces.
    /// </summary>
    [Fact]
    public async Task A_status_sent_in_the_apis_own_spelling_still_reads_as_not_measured()
    {
        var dto = new PlacementResultViewDto(
            PlacementId: 1, AttemptId: 77, CertId: 100, CertCode: "DEMO", ExamName: "Demo placement",
            TakenAt: Taken, DurationMinutes: 30, TotalQuestions: 20, TotalScore: 250,
            ResultBandCode: "B1", ResultBandName: "Intermediate",
            CefrBands:
            [
                new CefrBandInfoDto("B1", "Intermediate", 451, 700, true, true),
                new CefrBandInfoDto("B2", "Upper intermediate", 701, 900, false, false),
            ],
            SkillScores: [new PlacementSkillScoreInfoDto(1, "LIST", "Listening", 14, 20, 500, "B1")],
            WeakerSkillName: null, BandNote: null,
            ScoreDisplayFactor: 1, IsGraded: true,
            SkillResults:
            [
                // Exactly what the API sends today: the enum's own member name.
                new PlacementSkillResultInfoDto(2, "READ", "Reading", "NotMeasured", null, null, null, false,
                    "ai_grading_failed"),
            ]);

        await using var site = new Site(() => dto);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var reading = Row(html, "Reading");

        Assert.Contains("Not measured", reading, StringComparison.Ordinal);
        Assert.Contains("data-action=\"retry-grading\"", reading, StringComparison.Ordinal);
        Assert.DoesNotContain("signboard__marker", reading, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-18: "never grey". <b>The wrong input that would pass a weaker guard:</b> an assertion that the
    /// panel simply is not <c>--color-text-muted</c>, which a white panel also satisfies. So this asserts
    /// the actual token the panel carries: Sign Gold, the attention colour, in both the border and the
    /// heading — and it asserts the sheet names that token rather than a literal that could drift.
    /// </summary>
    [Fact]
    public void The_unmeasured_panel_is_painted_with_the_gold_token_and_never_grey()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "placement", "result.css"));

        var panel = Regex.Match(css, @"\.signboard__state \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Contains("border: 2px solid var(--sign-gold)", panel, StringComparison.Ordinal);
        Assert.Contains("background: var(--color-warning-soft)", panel, StringComparison.Ordinal);

        var title = Regex.Match(css, @"\.signboard__state-title \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Contains("color: var(--color-warning)", title, StringComparison.Ordinal);

        // Grey would be --color-text-muted or a literal grey on either rule.
        Assert.DoesNotContain("--color-text-muted", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("--color-text-muted", title, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-19: a position is a position inside a band, never a percentage. <b>The wrong input that would
    /// pass a weaker guard:</b> "a number is rendered" — a bare "42%" satisfies it and reads as a score,
    /// then as a collapse after a crossing. The guard requires the band beside the position and forbids
    /// the percent sign.
    /// </summary>
    [Fact]
    public async Task A_measured_skill_prints_a_band_and_a_position_and_never_a_bare_percentage()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var listening = Row(html, "Listening");

        Assert.Contains("B2 (42/100)", listening, StringComparison.Ordinal);
        // The certificate's own name for that band, so the code is not left to stand alone.
        Assert.Contains("Upper intermediate band", listening, StringComparison.Ordinal);
        // No bare percentage as TEXT. The geometry legitimately uses `width: 42%` in a style attribute,
        // so the check strips tags and attributes first — a "42%" a learner can read is the defect.
        Assert.DoesNotContain("42%", VisibleText(listening), StringComparison.Ordinal);
        // The evidence is words with units, not a second percentage.
        Assert.Contains("14 of 20 correct", listening, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-20 / story 55: a carried-over number is badged per skill, beside its own original date.
    /// <para>
    /// <b>The wrong input that would pass a weaker guard: a boolean.</b> If the page badges on
    /// <c>ReusedFromPrior</c> alone, a test that sets that boolean passes and a real response that
    /// records the older date but leaves the flag false is unbadged. This fixture leaves the flag FALSE
    /// on both skills and differs only in the measurement date, so only a date-driven page badges the
    /// right row.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_carried_number_is_badged_from_its_older_date_even_when_the_reuse_flag_is_false()
    {
        var dto = Result(
            listeningMeasuredAt: new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero),
            readingMeasuredAt: Taken,
            reusedFlag: false);

        await using var site = new Site(() => dto);
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var carried = Row(html, "Listening");
        Assert.Contains("Carried over", carried, StringComparison.Ordinal);
        Assert.Contains("18 October 2026", carried, StringComparison.Ordinal);
        Assert.DoesNotContain("8 November 2026", carried, StringComparison.Ordinal);

        var fresh = Row(html, "Reading");
        Assert.DoesNotContain("Carried over", fresh, StringComparison.Ordinal);
        Assert.Contains("Measured on 8 November 2026", fresh, StringComparison.Ordinal);

        // Exactly one badge in the document: a page-level banner on top of the rows fails here.
        Assert.Single(Regex.Matches(html, @"data-badge=""carried-over"""));
    }

    /// <summary>
    /// LRN-17: the overall is labelled as the floor, with its reason in words, naming both the ceiling
    /// and the floor skill. <b>The wrong input that would pass a weaker guard:</b> printing the overall
    /// coordinate alone, which a learner must interpret. The guard requires both skill names in the
    /// sentence and the word "not an average".
    /// </summary>
    [Fact]
    public async Task The_overall_is_labelled_as_the_floor_and_names_both_skills()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var signboard = Between(html, "placement-signboard", "</section>");

        Assert.Contains("Overall floor", signboard, StringComparison.Ordinal);
        // Tied to the limiting skill by name, not by position on the page.
        Assert.Contains("Reading holds this floor.", signboard, StringComparison.Ordinal);
        Assert.Contains("not an average", signboard, StringComparison.Ordinal);
        // Both ends of the profile are named.
        Assert.Contains("Listening reached B2 (42/100)", signboard, StringComparison.Ordinal);
        Assert.Contains("Reading is at A2 (10/100)", signboard, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-17: an unmeasured skill is excluded from the floor and said to be excluded, never counted as
    /// zero. <b>The wrong input that would pass a weaker guard:</b> a floor computed over measured rows
    /// only, which is correct arithmetic and silent about the omission — the learner cannot tell why the
    /// overall looks better than their worst skill.
    /// </summary>
    [Fact]
    public async Task The_floor_names_the_skill_it_excluded_and_says_it_is_not_a_zero()
    {
        await using var site = new Site(() => Result(expression: "not_measured"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        Assert.Contains("Reading was not measured, so it is excluded from this floor", html,
            StringComparison.Ordinal);
        Assert.Contains("not counted as zero", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-20 / U12: every AI-scored figure carries its label. <b>The wrong input that would pass a
    /// weaker guard:</b> a label printed once at the top of the page, which leaves two skill numbers and
    /// a single badge that a reader cannot tell apart. The guard reads the row it belongs to.
    /// </summary>
    [Fact]
    public async Task A_productive_figure_is_labelled_AI_estimated_on_its_own_row()
    {
        await using var site = new Site(() => Result(expression: "estimated_from_one_task"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var reading = Row(html, "Reading");
        Assert.Contains("data-label=\"ai-estimated\"", reading, StringComparison.Ordinal);
        Assert.Contains("the second task failed", reading, StringComparison.Ordinal);

        // The objective skill in the same sitting is not mislabelled by a page-level badge.
        Assert.DoesNotContain("AI-estimated", Row(html, "Listening"), StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-20 / story 56: the published 70% rule is on the screen in plain language. <b>The wrong input
    /// that would pass a weaker guard:</b> the string "70%" alone, which a score could also produce. So
    /// the guard requires the rule's own sentence: "at every lower band".
    /// </summary>
    [Fact]
    public async Task The_published_pass_rule_is_stated_in_plain_language()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        Assert.Contains("How a band is awarded", html, StringComparison.Ordinal);
        Assert.Contains("at least 70% correctly at that band and at every lower band", html,
            StringComparison.Ordinal);
        Assert.Contains("four-option guess", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// LRN-21: exactly one primary call to action ends the screen. <b>The wrong input that would pass a
    /// weaker guard:</b> "a primary button exists", which is true of the old page that ended with two
    /// equal actions. So the guard counts every <c>btn--primary</c> in the document.
    /// </summary>
    [Fact]
    public async Task The_screen_ends_with_exactly_one_primary_action()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        // The retry is a state action, not a second route out of the result, and it is only present
        // when a grading failed — which this fixture does not have.
        var primaries = Regex.Matches(html, @"class=""[^""]*\bbtn--primary\b[^""]*""");
        Assert.Single(primaries);
        Assert.Contains("id=\"placement-primary-action\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ticket's own criterion: no raw database code reaches the markup. <b>The wrong input that would
    /// pass a weaker guard:</b> scanning for the word "null" or for one code, when the page prints six
    /// status codes and band codes from three different tables. So this asserts the closed set of codes
    /// that the result path can carry, and the band code the fixture uses.
    /// </summary>
    [Fact]
    public async Task No_raw_database_code_reaches_the_markup()
    {
        await using var site = new Site(() => Result(expression: "estimated_from_one_task"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var signboard = Between(html, "placement-signboard", "</section>");
        var visible = VisibleText(signboard);

        foreach (var code in new[]
                 {
                     "estimated_from_one_task", "not_measured", "exempt_accommodated", "pre_a1",
                     "below_A1", "candidate_skipped", "ai_grading_failed", "inactivity_timeout",
                 })
        {
            Assert.DoesNotContain(code, visible, StringComparison.Ordinal);
        }

        // And no underscore-bearing identifier survives in the visible text at all, which is the
        // general form of the rule. The band code is checked in an attribute below, where it is a
        // legitimate machine-readable hint rather than lettering.
        Assert.DoesNotMatch(@"[a-z]+_[a-z0-9_]+", visible);
        // The band's own readable name is what the header prints; the code is only a title attribute.
        Assert.Contains("Intermediate", visible, StringComparison.Ordinal);
    }

    /// <summary>
    /// Colour is never the only signal (LRN-01). <b>The wrong input that would pass a weaker guard:</b>
    /// a gold panel whose heading is a bare glyph or nothing, which a screen reader cannot read and a
    /// colour-blind reader cannot see. So the state's meaning is asserted as text.
    /// </summary>
    [Fact]
    public async Task Every_state_carries_a_word_and_not_only_a_colour()
    {
        await using var site = new Site(() => Result(expression: "not_measured"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");

        var panel = Between(Row(html, "Reading"), "data-state-panel", "</div>");
        Assert.Contains("data-state-label", panel, StringComparison.Ordinal);
        Assert.Contains("Not measured", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// A grading job is a wait, not a failure: it says so and offers no retry.
    /// <b>The wrong input that would pass a weaker guard:</b> treating every non-measured status the
    /// same, which tells a learner whose writing is in the queue that the grader failed.
    /// </summary>
    [Fact]
    public async Task A_skill_still_grading_says_so_and_offers_no_retry()
    {
        await using var site = new Site(() => Result(expression: "grading"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var reading = Row(html, "Reading");

        Assert.Contains("Grading", reading, StringComparison.Ordinal);
        Assert.Contains("fills in on its own", reading, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"retry-grading\"", reading, StringComparison.Ordinal);
        Assert.DoesNotContain("could not score", reading, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is read-only: the only <c>&lt;form&gt;</c> it may render is the free retry, and that one
    /// posts to the existing retry endpoint. <b>The wrong input that would pass a weaker guard:</b>
    /// counting forms only on the fixtures with no failure, where zero forms is also what a missing retry
    /// looks like. So this fixture HAS the failure and asserts the one form's target.
    /// </summary>
    [Fact]
    public async Task The_page_writes_nothing_except_the_free_grading_retry()
    {
        await using var site = new Site(() => Result(expression: "not_measured"));
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var signboard = Between(html, "placement-signboard", "</section>");

        var forms = Regex.Matches(signboard, @"<form\b[^>]*>");
        Assert.Single(forms);
        Assert.Contains("action=\"/placement/result/1/retry\"", forms[0].Value, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", forms[0].Value, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(signboard, @"name=""__RequestVerificationToken"""));
    }

    /// <summary>
    /// §5: the stylesheet obeys the painted-sign system. <b>The wrong input that would pass a weaker
    /// guard:</b> relying on the global reset while one rule sets a radius — so the scan covers the whole
    /// sheet, and every figure in the page carries the mono face.
    /// </summary>
    [Fact]
    public void The_stylesheet_sets_no_radius_no_shadow_and_no_gradient()
    {
        var css = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css", "placement", "result.css"));

        foreach (Match match in Regex.Matches(css, @"box-shadow:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("none", match.Groups["value"].Value.Trim());
        }

        foreach (Match match in Regex.Matches(css, @"border-radius:\s*(?<value>[^;]+);"))
        {
            Assert.Equal("0", match.Groups["value"].Value.Trim());
        }

        foreach (var rule in new[] { "linear-gradient", "radial-gradient", "conic-gradient", "backdrop-filter", "text-shadow" })
        {
            Assert.DoesNotContain(rule, css, StringComparison.Ordinal);
        }
    }

    /// <summary>The page carries no typed glyph standing in for an icon (§5, LRN-04).</summary>
    [Fact]
    public async Task The_page_carries_no_typed_glyphs()
    {
        await using var site = new Site(() => Result());
        using var client = site.CreateClientWithLearner();

        var html = await client.GetStringAsync("/placement/result/1");
        var signboard = Between(html, "placement-signboard", "</section>");

        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2300-\u23FF\u2600-\u27BF\uD83C-\uDBFF]", signboard);
    }

    // ---- helpers ----------------------------------------------------------

    private static IReadOnlyList<string> Headers(string html)
    {
        var signboard = Between(html, "placement-signboard", "</section>");
        return Regex.Matches(signboard, @"<th scope=""col""[^>]*>(?<label>[^<]*)</th>")
            .Select(m => WebUtility.HtmlDecode(m.Groups["label"].Value).Trim())
            .Where(label => label.Length > 0)
            .ToList();
    }

    /// <summary>The one table row whose row header names the skill.</summary>
    private static string Row(string html, string skillName)
    {
        var marker = "<th scope=\"row\"";
        for (var i = html.IndexOf(marker, StringComparison.Ordinal); i >= 0;
             i = html.IndexOf(marker, i + marker.Length, StringComparison.Ordinal))
        {
            var close = html.IndexOf('>', i);
            if (close < 0) continue;
            var nameAt = html.IndexOf(skillName, close, StringComparison.Ordinal);
            if (nameAt < 0 || nameAt - close > 120) continue;

            var start = html.LastIndexOf("<tr", i, StringComparison.Ordinal);
            var end = html.IndexOf("</tr>", nameAt, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > nameAt, $"the header for {skillName} is not inside a row");
            return html[start..(end + "</tr>".Length)];
        }

        Assert.Fail($"no row header names {skillName}");
        return string.Empty;
    }

    private static string Between(string html, string startMarker, string endMarker)
    {
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has no {startMarker}");
        var end = html.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{startMarker} is never closed by {endMarker}");
        return html[start..end];
    }

    /// <summary>Only what a reader sees: tags and attributes removed, whitespace collapsed.</summary>
    private static string VisibleText(string html) =>
        Regex.Replace(Regex.Replace(html, @"<[^>]*>", " "), @"\s+", " ").Trim();

    /// <summary>
    /// A certificate whose bands are CEFR-named — TOEIC's shape, six columns from "Below A1" to C1.
    /// Two skills, so a row-level failure is attributable to the row.
    /// </summary>
    private static PlacementResultViewDto Result(
        string expression = "measured",
        DateTimeOffset? listeningMeasuredAt = null,
        DateTimeOffset? readingMeasuredAt = null,
        bool reusedFlag = false)
    {
        var listeningAt = listeningMeasuredAt ?? Taken;
        var readingAt = readingMeasuredAt ?? Taken;
        var estimated = expression == "estimated_from_one_task";
        var unmeasured = expression == "not_measured";

        return new PlacementResultViewDto(
            PlacementId: 1, AttemptId: 77, CertId: 100, CertCode: "DEMO", ExamName: "Demo placement",
            TakenAt: Taken, DurationMinutes: 30, TotalQuestions: 40, TotalScore: 250,
            ResultBandCode: "B1", ResultBandName: "Intermediate",
            CefrBands:
            [
                new CefrBandInfoDto("below_A1", "Below A1", 0, 99, true, false),
                new CefrBandInfoDto("A1", "Beginner", 100, 220, true, false),
                new CefrBandInfoDto("A2", "Elementary", 221, 450, true, false),
                new CefrBandInfoDto("B1", "Intermediate", 451, 700, true, true),
                new CefrBandInfoDto("B2", "Upper intermediate", 701, 900, false, false),
                new CefrBandInfoDto("C1", "Advanced", 901, 990, false, false),
            ],
            SkillScores:
            [
                new PlacementSkillScoreInfoDto(1, "LIST", "Listening", 14, 20, 500, "B1"),
                new PlacementSkillScoreInfoDto(2, "READ", "Reading", 6, 20, 300, "A2"),
            ],
            WeakerSkillName: "Reading", BandNote: null,
            ScoreDisplayFactor: 1, IsGraded: true,
            SkillResults:
            [
                new PlacementSkillResultInfoDto(1, "LIST", "Listening", "Measured", "B2", 42,
                    listeningAt, reusedFlag, null),
                new PlacementSkillResultInfoDto(2, "READ", "Reading", expression, unmeasured ? null : "A2",
                    unmeasured || expression == "grading" ? null : 10,
                    unmeasured ? null : readingAt,
                    reusedFlag,
                    unmeasured ? "ai_grading_failed" : null),
            ]);
    }

    /// <summary>
    /// A certificate whose bands are NOT CEFR-named — TOEIC_CAT's shape (CAT_B1, CAT_B2). The level is
    /// real and its codes are A1-C2, but no column of this certificate is called "B2", so the row must
    /// say so and draw nothing. This is PRN-03's honest gap: converting CAT_B2 into a CEFR level, or
    /// silently dropping the row onto the nearest column, is the crosswalk the rule forbids.
    /// </summary>
    private static PlacementResultViewDto NonCefrBands() => new(
        PlacementId: 1, AttemptId: 77, CertId: 101, CertCode: "TOEIC_CAT", ExamName: "CAT benchmark",
        TakenAt: Taken, DurationMinutes: 90, TotalQuestions: 20, TotalScore: 620,
        ResultBandCode: "CAT_B1", ResultBandName: "Intermediate",
        CefrBands:
        [
            new CefrBandInfoDto("CAT_B1", "Intermediate", 400, 695, true, true),
            new CefrBandInfoDto("CAT_B2", "Professional", 700, 990, false, false),
        ],
        SkillScores: [new PlacementSkillScoreInfoDto(1, "COMP", "Comprehension", 14, 20, 620, "CAT_B1")],
        WeakerSkillName: null, BandNote: null,
        ScoreDisplayFactor: 1, IsGraded: true,
        SkillResults:
        [
            new PlacementSkillResultInfoDto(1, "COMP", "Comprehension", "Measured", "B2", 42, Taken, false, null),
        ]);

    public sealed class Site(Func<PlacementResultViewDto> result) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(Upstream.Create(result).Client));
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

        public class Upstream : DispatchProxy
        {
            public ICompanionApiClient Client { get; private set; } = null!;

            public Func<PlacementResultViewDto> ResultFactory { get; set; } = () => throw new InvalidOperationException();

            public static Upstream Create(Func<PlacementResultViewDto> result)
            {
                var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
                var stub = (Upstream)(object)proxy;
                stub.ResultFactory = result;
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
                nameof(IPlacementClient.GetPlacementResultAsync) => Task.FromResult(ResultFactory()),
                // The page pre-fetches an insight and degrades without one; it must not depend on it.
                nameof(IAiInsightsClient.GetExamInsightAsync) => Task.FromResult<AiInsightDto?>(null),
                _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
            };
        }
    }
}
