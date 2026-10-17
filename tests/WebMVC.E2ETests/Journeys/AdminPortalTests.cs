using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminPortalTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Allow_Admin_Access_To_Management_Dashboard()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/Admin");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Admin", RegexOptions.IgnoreCase));

        // Verify Admin Header and Title
        await Expect(Page.Locator("h1:has-text('Dashboard'), strong:has-text('extra efficient Admin')").First).ToBeVisibleAsync();

        // Verify admin sections exist
        await Expect(Page.Locator(":text('User accounts'), :text('Users')").First).ToBeVisibleAsync();
    }
}
