using System.IO;
using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;
using Xunit;
namespace WebMVC.Tests;

/// <summary>
/// #159: the learner dashboard is three blocks, and the learning features have a door.
/// <para>
/// These read the markup rather than a rendered page because the complaint was structural - eleven
/// blocks where three belong, and Writing/Speaking missing from a seventeen-item sidebar. A block that
/// is still in the source is still a block, whether or not it happens to be empty for one learner.
/// </para>
/// </summary>
public sealed class DashboardThreeBlocksTests
{
    private static string View(string name) =>
        File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", name));

    /// <summary>The markup without Razor comments: a comment explaining what must not come back is
    /// not the thing itself, and asserting over comments makes the test lie in both directions.</summary>
    private static string Markup(string html) => Regex.Replace(html, @"@\*[\s\S]*?\*@", "");

    private static string DashboardView() => string.Concat(
        Directory.EnumerateFiles(
                Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Dashboard"), "*.cshtml")
            .Select(File.ReadAllText));

    private static string LearnerLayout() => View(Path.Combine("Shared", "_LearnerLayout.cshtml"));

    [Fact]
    public void The_dashboard_renders_the_three_blocks()
    {
        var index = DashboardView();

        Assert.Contains("_DashboardHero", index);
        Assert.Contains("_DashboardGoal", index);
        Assert.Contains("_DashboardSkills", index);
    }

    /// <summary>
    /// #241 replaced the fixed three includes with a loop over <c>Model.Blocks</c>, which is what makes
    /// "exactly three" testable rather than aspirational: there is no place to put a fourth partial
    /// tag, because the view has exactly one.
    /// </summary>
    [Fact]
    public void The_three_block_partials_are_named_in_the_block_list()
    {
        var index = DashboardView();

        Assert.Contains("Model.Blocks", index);
        Assert.Contains("data-block-count", index);
        // The partials are named in the list, not in the view: one loop renders whatever the list
        // says, so a fourth block is an entry here and a count test rather than a stray tag.
        Assert.Equal(
            ["_DashboardHero", "_DashboardGoal", "_DashboardSkills"],
            DashboardBlocks.Placed);
        Assert.Equal(["_DashboardHero"], DashboardBlocks.Unplaced);
    }

    [Theory]
    // The eight the ticket names, each by the marker only that block carried. The verification
    // alert is not one of them: buying credits fails silently until the address is confirmed, so
    // that notice stays as a dismissible alert rather than a block with a heading of its own.
    [InlineData("Mentor Boarding Pass")]
    [InlineData("1-on-1 Mentor Session")]
    [InlineData("_DashboardMentorPass")]
    [InlineData("Today's Directive")]
    [InlineData("_DashboardMission")]
    [InlineData("Recent exams")]
    [InlineData("Recent practice")]
    [InlineData("_DashboardActivity")]
    [InlineData("_DashboardWeaknessRadar")]
    [InlineData("readiness-strip")]
    [InlineData("_DashboardReadiness")]
    public void The_eight_named_blocks_are_gone_from_the_markup(string marker)
    {
        Assert.DoesNotContain(marker, Markup(DashboardView()));
    }

    [Fact]
    public void The_dashboard_renders_no_ai_insight_panel()
    {
        // The AI Study Mentor block is one of the eight the ticket deletes. Asserted separately from
        // the block count because the panel is a Shared partial, so re-adding one line would not
        // change how many _Dashboard partials the page includes.
        var index = View(Path.Combine("Dashboard", "Index.cshtml"));

        Assert.DoesNotContain("_AiInsightPanel", index);
        Assert.DoesNotContain("InsightPanel", index);
    }

    [Fact]
    public void The_dashboard_has_no_fourth_block()
    {
        // "Exactly three blocks" is the ticket. #241 moved the count out of the view and onto a list,
        // so this counts what the page RENDERS (the list it loops) rather than how many `<partial>`
        // strings a view happens to contain - and the view has exactly one partial tag, which renders
        // whatever the list holds.
        var index = View(Path.Combine("Dashboard", "Index.cshtml"));
        var tags = Regex.Matches(index, @"<partial\s+name=""@block""").Count;

        Assert.Equal(1, tags);
        Assert.Equal(3, DashboardBlocks.Placed.Count);
        Assert.Equal(1, DashboardBlocks.Unplaced.Count);
        // The three are distinct partials, so "three blocks" cannot be one block rendered thrice.
        Assert.Equal(3, DashboardBlocks.Placed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Skill_cards_render_one_card_per_skill_of_the_certificate()
    {
        // #149's rule, applied to the dashboard: the rows are Cert.Skills, never a fixed pair. Each
        // row is named from the skill the service sent, so a certificate that calls its skills
        // Comprehension and Expression gets those two columns, not Listening/Reading.
        // #241: this is now a table whose COLUMN COUNT is the certificate's count, so the assertion is
        // on Model.Skills.Columns - one column per examined skill - and never on a fixed grid.
        var card = Markup(View(Path.Combine("Dashboard", "_DashboardSkills.cshtml")));

        Assert.Contains("table.Columns", card);
        Assert.Contains("@column.Name", card);
        // Only the two skill names the ticket names as the defect; the modality switch may still
        // mention Speaking/Writing because those open their own flow.
        Assert.DoesNotContain("Listening", card);
        Assert.DoesNotContain("Reading", card);
        // #241: no fixed four-track template, and no auto-fit that would silently invent a fourth.
        Assert.DoesNotContain("auto-fit", card);
    }

    [Fact]
    public void The_hero_shows_the_next_action_and_not_a_greeting()
    {
        var hero = Markup(View(Path.Combine("Dashboard", "_DashboardHero.cshtml")));

        Assert.Contains("Model.Dashboard.NextAction", hero);
        Assert.Contains("hero.ActionLabel", hero);
        Assert.Contains("hero.ActionUrl", hero);
        Assert.DoesNotContain("Hello, @", DashboardView());
    }

    [Fact]
    public void The_sidebar_has_five_items_including_writing_and_speaking()
    {
        var layout = LearnerLayout();
        var nav = Regex.Match(layout, @"<nav class=""nav-list""[^>]*>(?<body>[\s\S]*?)</nav>").Groups["body"].Value;
        Assert.NotEmpty(nav);

        var items = Regex.Matches(nav, @"<a class=""@NavClass\(""(?<href>[^""]+)""\)").Select(m => m.Groups["href"].Value).ToList();

        Assert.Equal(5, items.Count);
        Assert.Contains("/dashboard", items);
        Assert.Contains("/exams", items);
        Assert.Contains("/practice", items);
        Assert.Contains("/writing", items);
        Assert.Contains("/speaking", items);
    }

    [Fact]
    public void Writing_and_speaking_are_not_behind_a_submenu()
    {
        // "Do not put them behind a submenu": the entry is an anchor in the rail, not a <details>
        // group or a button that opens a second list.
        var layout = LearnerLayout();
        var nav = Regex.Match(layout, @"<nav class=""nav-list""[^>]*>(?<body>[\s\S]*?)</nav>").Groups["body"].Value;

        Assert.DoesNotContain("<details", nav);
        Assert.Contains("href=\"/writing\"", nav);
        Assert.Contains("href=\"/speaking\"", nav);
    }
}
