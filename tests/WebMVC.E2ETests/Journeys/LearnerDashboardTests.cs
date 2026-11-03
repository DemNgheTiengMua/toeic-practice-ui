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

        // Block 2 of 3: the goal standing figures, including the target the learner set.
        var goal = Page.Locator("section.dashboard-goal");
        await Expect(goal).ToBeVisibleAsync();
        await Expect(Page.Locator("#dashboard-goal-title")).ToContainTextAsync("Goal Standing");
        await Expect(goal.GetByText("Current band")).ToBeVisibleAsync();
        await Expect(goal.GetByText("Target band")).ToBeVisibleAsync();
        Assert.That(await goal.Locator(".dashboard-goal__figure").CountAsync(), Is.GreaterThan(0),
            "The goal block must print at least one standing figure.");

        // Block 3 of 3: one card per skill the learner's certificate actually has.
        var expectedSkillCount = await ActiveCertificateSkillCountAsync("learner@demo.local");
        Assert.That(expectedSkillCount, Is.GreaterThan(0), "The seeded certificate must declare skills.");
        var skillCards = Page.Locator("section.dashboard-skills article.dashboard-skill");
        await Expect(skillCards.First).ToBeVisibleAsync();
        Assert.That(await skillCards.CountAsync(), Is.EqualTo(expectedSkillCount),
            "The dashboard must render exactly one skill card per skill of the active certificate.");
        await Expect(Page.Locator("#dashboard-skills-title")).ToContainTextAsync("Skill Cards");
        await Expect(skillCards.First.Locator("a.btn")).ToBeVisibleAsync();

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
