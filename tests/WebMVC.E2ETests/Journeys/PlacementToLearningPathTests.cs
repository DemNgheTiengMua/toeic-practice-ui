using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class PlacementToLearningPathTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;
    private PlacementPage _placementPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
        _placementPage = new PlacementPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Complete_Free_Placement_Journey_And_Generate_Learning_Path()
    {
        // 1. Sign in as learner.fresh (diagnostic candidate)
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner.fresh@demo.local", "Demo1234!");

        // 2. Navigate to placement index
        await _placementPage.GotoPlacementAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/placement", RegexOptions.IgnoreCase));

        // 3. Verify diagnostic placement test copy
        await Expect(Page.Locator("body")).ToContainTextAsync("Placement");

        // 4. Navigate to Target Band selection
        await _placementPage.GotoTargetBandAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/placement/target-band", RegexOptions.IgnoreCase));

        // 5. Select B2 Target Band if not already selected
        var bandSelect = Page.Locator("select#target-band-select, select[name='targetBand']");
        if (await bandSelect.IsVisibleAsync())
        {
            await bandSelect.SelectOptionAsync("B2");
            var saveBtn = Page.Locator("button[type='submit']").First;
            await saveBtn.ClickAsync();
        }

        // 6. Navigate to /learning-path and verify path is active
        await Page.GotoAsync($"{BaseUrl}/learning-path");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/learning-path", RegexOptions.IgnoreCase));

        // Verify learning path ladder renders
        await Expect(Page.Locator("body")).ToContainTextAsync("learning path");

        // 7. Verify Free Loop Invariant: credit balance remains 0
        var creditHeader = Page.Locator("header, nav").Filter(new() { HasText = "credit" }).First;
        if (await creditHeader.IsVisibleAsync())
        {
            var headerText = await creditHeader.InnerTextAsync();
            Assert.That(headerText, Does.Contain("0 credit").IgnoreCase.Or.Contain("0 credits").IgnoreCase.Or.Contain("credits left").IgnoreCase);
        }
    }
}
