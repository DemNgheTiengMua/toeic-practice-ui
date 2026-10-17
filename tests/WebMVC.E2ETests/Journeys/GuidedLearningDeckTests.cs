using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class GuidedLearningDeckTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Walk_Through_All_Cards_And_Validate_MicroCheck_Question()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");

        // Open lesson 1 directly
        await Page.GotoAsync($"{BaseUrl}/lessons/1");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/lessons/1"));
        await Expect(Page.Locator("h1:has-text('Present Continuous Tense')")).ToBeVisibleAsync();

        // Check cards container presence
        var deckContainer = Page.Locator(".lesson-deck");
        await Expect(deckContainer).ToBeVisibleAsync();

        // If card 4 is not already visible, click Next until Card 4 is reached
        var card4 = Page.Locator("#card-4");
        var nextBtn = Page.Locator("#btnNext");

        for (int i = 0; i < 4; i++)
        {
            if (await card4.IsVisibleAsync()) break;
            if (await nextBtn.IsVisibleAsync() && !await nextBtn.IsDisabledAsync())
            {
                await nextBtn.ClickAsync();
            }
        }

        // Verify Micro-check card is visible
        await Expect(card4).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Check Your Understanding")).ToBeVisibleAsync();

        // Select option B: is evaluating
        var optionB = Page.Locator("label[data-opt='B']");
        await optionB.ClickAsync();

        // Submit formative check
        var checkAnswerBtn = Page.Locator("#btnCheckAnswer");
        await checkAnswerBtn.ClickAsync();

        // Verify pedagogical explanation appears
        var explanation = Page.Locator("#microCheckExplanation");
        await Expect(explanation).ToBeVisibleAsync();
        await Expect(explanation).ToContainTextAsync("at the moment");

        // Verify Return to path button is present
        await Expect(Page.Locator("#btnFinish")).ToBeVisibleAsync();
    }
}
