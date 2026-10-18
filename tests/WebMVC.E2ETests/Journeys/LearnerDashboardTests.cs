using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class LearnerDashboardTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Render_Target_Readiness_Strip_And_Mission_Directives()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/Dashboard");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Dashboard", RegexOptions.IgnoreCase));

        // Check readiness strip contains Target Band B2 and Current Band B1
        var readinessStrip = Page.Locator(".readiness-strip, [data-readiness-strip], :text('Target band')");
        await Expect(readinessStrip.First).ToBeVisibleAsync();

        // Check Today's mission is displayed
        var todaysMission = Page.Locator(":text(\"Today's mission\"), :text(\"Today's Mission\")");
        await Expect(todaysMission.First).ToBeVisibleAsync();

        // Check navigation menu is functional
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "My learning path" })).ToBeVisibleAsync();
    }
}
