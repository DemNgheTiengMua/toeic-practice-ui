using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class LearnerDashboardTests : PlaywrightTestBase
{
    /// <summary>
    /// #169: the dashboard is three blocks (#159): a hero carrying the one next action, the goal
    /// standing figures, and one card per skill of the active certificate. The readiness strip and
    /// "Today's Directive" this journey used to look for were deleted with the other eight blocks,
    /// so the assertion moved to the blocks that replaced them rather than to nothing.
    /// </summary>
    [Test]
    public async Task Should_Render_The_Three_Dashboard_Blocks_The_Dashboard_Has_Now()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/Dashboard");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Dashboard", RegexOptions.IgnoreCase));

        // Block 1 of 3: the hero is a placard with exactly one action on it.
        var hero = Page.Locator("section.dashboard-hero");
        await Expect(hero).ToBeVisibleAsync();
        await Expect(Page.Locator("#dashboard-hero-title")).ToBeVisibleAsync();
        Assert.That((await Page.Locator("#dashboard-hero-title").InnerTextAsync()).Trim(), Is.Not.Empty,
            "The hero must name the one next action in its title.");
        var heroAction = hero.Locator("a.btn--primary");
        await Expect(heroAction).ToHaveCountAsync(1);
        await Expect(heroAction).ToBeVisibleAsync();
        Assert.That(await heroAction.GetAttributeAsync("href"), Is.Not.Null.And.Not.Empty,
            "The hero's action must lead somewhere.");

        // Block 2 of 3: goal standing, as FOUR TABULAR CELLS and no progress bar (#241 / D21a).
        // The labels are the four the block renders; a "Current band"/"Target band" pair was the
        // pre-#241 card layout, and asserting it now would be asserting a page that was deleted.
        var goal = Page.Locator("section.dashboard-goal");
        await Expect(goal).ToBeVisibleAsync();
        await Expect(Page.Locator("#dashboard-goal-title")).ToContainTextAsync("Goal Standing");

        var goalCells = goal.Locator("table.dashboard-goal__cells tbody tr");
        Assert.That(await goalCells.CountAsync(), Is.EqualTo(4),
            "the goal block is exactly four cells: level against target, hours, pacing, verdict");

        await Expect(goal.GetByText("Level against target")).ToBeVisibleAsync();
        await Expect(goal.GetByText("Remaining guided hours")).ToBeVisibleAsync();
        await Expect(goal.GetByText("Pacing")).ToBeVisibleAsync();
        await Expect(goal.GetByText("Verdict")).ToBeVisibleAsync();

        // A progress bar is what #241 removed; a styled div with a width is the mechanism, and it
        // must not be back.
        Assert.That(await goal.Locator("[role='progressbar'], progress").CountAsync(), Is.EqualTo(0),
            "the goal block carries no progress bar mechanism");

        // Block 3 of 3: per-skill standing, one COLUMN per skill the learner's certificate actually
        // has. #241 replaced the card grid with a table: a `repeat(auto-fit, ...)` grid silently
        // accepts a fourth track, while a table's header is the certificate's own list so a skill the
        // paper does not examine has no column to leave empty.
        var expectedSkillCount = await ActiveCertificateSkillCountAsync("learner@demo.local");
        Assert.That(expectedSkillCount, Is.GreaterThan(0), "The seeded certificate must declare skills.");

        var skillTable = Page.Locator("section.dashboard-skills table#skill-standing");
        await Expect(skillTable).ToBeVisibleAsync();

        var skillColumns = skillTable.Locator("thead th.skill-standing__head");
        Assert.That(await skillColumns.CountAsync(), Is.EqualTo(expectedSkillCount),
            "The dashboard must render exactly one skill column per skill of the active certificate.");

        var skillCells = skillTable.Locator("tbody td[data-skill-id]");
        Assert.That(await skillCells.CountAsync(), Is.EqualTo(expectedSkillCount),
            "Every skill column needs its own standing cell.");

        await Expect(Page.Locator("#dashboard-skills-title")).ToContainTextAsync("Per-Skill Standing");

        // Each cell is either a measured figure or the words "Not measured yet" with a route to fix
        // it. Never a bare "A1" and never a dash - #211 keeps an unmeasured skill absent rather than
        // present at the bottom band.
        for (var i = 0; i < expectedSkillCount; i++)
        {
            var cell = skillCells.Nth(i);
            var measured = await cell.Locator(".skill-standing__level").CountAsync();
            var unmeasured = await cell.Locator(".skill-standing__unmeasured").CountAsync();
            Assert.That(measured + unmeasured, Is.EqualTo(1),
                "a skill cell must be either measured or explicitly unmeasured");
            if (unmeasured > 0)
            {
                await Expect(cell.Locator("a.skill-standing__action")).ToHaveAttributeAsync("href", "/placement");
            }
        }

        // The page still carries its main navigation.
        await Expect(Page.Locator("nav[aria-label='Main navigation']")).ToBeVisibleAsync();
    }

    /// <summary>Skills of the learner's active certificate - the count the skill block must render.</summary>
    private static async Task<int> ActiveCertificateSkillCountAsync(string email)
    {
        await using var connection = await OpenAppDatabaseAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM Skills
            WHERE CertId = (SELECT ActiveCertId FROM Users WHERE Email = @email);
            """;
        command.Parameters.AddWithValue("@email", email);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
