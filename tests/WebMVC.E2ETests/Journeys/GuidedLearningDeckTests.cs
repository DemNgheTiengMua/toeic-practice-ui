using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class GuidedLearningDeckTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;
    private DeckPlayerPage _deckPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
        _deckPage = new DeckPlayerPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Walk_Through_All_Cards_And_Validate_MicroCheck_Question()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");

        // Open lesson 1 directly
        await _deckPage.GotoAsync(1);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/lessons/1"));
        await Expect(Page.Locator("h1:has-text('Present Continuous Tense')")).ToBeVisibleAsync();

        // Check cards container presence
        await Expect(_deckPage.DeckContainer).ToBeVisibleAsync();

        // Navigate forward until Card 4 (Micro-check) is reached
        await _deckPage.NavigateToMicroCheckCardAsync();

        // Verify Micro-check card is visible
        await Expect(_deckPage.MicroCheckCard).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Check Your Understanding")).ToBeVisibleAsync();

        // Select option B and submit formative check
        await _deckPage.AnswerMicroCheckAsync("B");

        // Verify pedagogical explanation appears
        await Expect(_deckPage.ExplanationContainer).ToBeVisibleAsync();
        await Expect(_deckPage.ExplanationContainer).ToContainTextAsync("at the moment");

        // Verify Return to path button is present
        await Expect(_deckPage.FinishButton).ToBeVisibleAsync();
    }
}
