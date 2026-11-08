using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// #209 criterion 4: a learner answers an item that arrived through the authoring pipeline.
/// <para>
/// <b>What is real and what is arranged.</b> The cell is populated <em>outside</em> the browser by
/// <see cref="OneCellFixture"/>, which drives the same production seams the service tests drive:
/// upload, the four gates, <c>validated</c>, the stratified audit's decision, <c>active</c>. It is not
/// seeded <c>active</c>. The browser half then signs in as the demo learner and answers the cell
/// through the practice screen, and the summary is asserted.
/// </para>
/// <para>
/// <b>Why the authoring half is not driven through the browser.</b> Measured on this deployment: the
/// API registers no <see cref="Services.IVerifierModel"/> and no <see cref="Services.ILevelClassifier"/>,
/// so gate three refuses every record with <c>gate3.distribution.unusable</c> and gate four with
/// <c>gate4.level.unavailable</c>. A browser upload therefore cannot produce a <c>validated</c> item at
/// all, and a journey that clicked through it would assert a refusal and call it authoring. The
/// pipeline half is proved where it can run - <c>OneCellEndToEndTests</c> in <c>Services.Tests</c> -
/// and the browser half starts from that pipeline's genuine output.
/// </para>
/// <para>
/// <b>Why this fixture is not parallelizable.</b> It writes to state the other journeys share: it fills
/// a real cell and drives the demo learner through a session on their path. Run alongside them, it
/// changes the page another journey is mid-way through, which is the same reason
/// <c>SittingAndPracticeQolTests</c> is marked the same way.
/// </para>
/// </summary>
[NonParallelizable]
[TestFixture]
public class OneCellLearnerJourneyTests : PlaywrightTestBase
{
    private const string TopicCode = "R-DET";
    private const string Level = "B1";

    [Test]
    public async Task A_learner_answers_the_promoted_cell_and_sees_a_graded_summary()
    {
        // Arrange: the cell, through the real pipeline, in the database the running app reads.
        var seeded = await OneCellFixture.PopulateReadingCellAsync(TopicCode, Level);

        Assert.That(seeded.ActiveItems, Is.GreaterThan(0),
            "the pipeline produced no active item, so the journey would be testing nothing");

        try
        {
            await DriveTheDrillAsync(seeded);
        }
        finally
        {
            // The journey learner is this fixture's own, so clearing its session is enough to make the
            // run repeatable; nothing the other journeys read is touched.
            await OneCellFixture.ResetJourneyLearnerAsync(seeded.Email);
        }
    }

    private async Task DriveTheDrillAsync(OneCellFixture.Populated seeded)
    {
        await SignInAsync(seeded.Email, seeded.Password);

        // The journey learner's path has one step at exactly this cell, so the browser can start a
        // drill against it without a cell-picker the product does not have yet (#238's surface).
        await Page.GotoAsync($"{BaseUrl}/learning-path");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/learning-path", RegexOptions.IgnoreCase));

        var step = Page.Locator($"form[action='/learning-path/steps/{seeded.StepId}/start'] button").First;
        await Expect(step).ToBeVisibleAsync();
        await step.ClickAsync();

        await Page.WaitForURLAsync(new Regex(@"/practice/\d+"), new() { Timeout = 20000 });
        var sessionUrl = Page.Url;

        // The item on screen is the authored one, rendered from its pinned version.
        var optionList = Page.Locator("#option-list");
        await Expect(optionList).ToBeVisibleAsync();
        await Expect(optionList.Locator(".js-option-radio")).ToHaveCountAsync(4);

        // The submit control cycles by label: "Check answer" grades the chosen option, "Next question"
        // advances, and the final one reads "View summary". The option labels are disabled once an
        // answer is graded, so each step waits for the control to move on before the next one - reading
        // it mid-render is what makes a naive loop click a disabled label.
        var answered = 0;
        for (var turn = 0; turn < 200; turn++)
        {
            if (Page.Url.Contains("/summary", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var submit = Page.Locator("#submit-btn");
            await Expect(submit).ToBeVisibleAsync();

            var label = (await submit.InnerTextAsync()).Trim();

            if (label.Contains("summary", StringComparison.OrdinalIgnoreCase))
            {
                await submit.ClickAsync();
                break;
            }

            if (label.Contains("Next", StringComparison.OrdinalIgnoreCase))
            {
                await submit.ClickAsync();
                await Expect(submit).ToContainTextAsync("Check answer", new() { Timeout = 15000 });
                continue;
            }

            // "Check answer": choose an ungraded option, then grade it. The radio is visually hidden
            // behind its label, which is what a learner clicks.
            var options = Page.Locator("#option-list label[id^='opt-label-']:not(.option--wrong):not(.option--correct)");
            if (await options.CountAsync() == 0)
            {
                break;
            }

            await options.First.ClickAsync();
            await Expect(submit).ToBeEnabledAsync();
            await submit.ClickAsync();
            answered++;

            // The graded state swaps the control to "Next question" or "View summary".
            await Expect(submit).Not.ToContainTextAsync("Check answer", new() { Timeout = 15000 });
        }

        Assert.That(answered, Is.GreaterThan(0), "the session served no question");

        await Page.GotoAsync($"{sessionUrl}/summary");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/practice/\d+/summary", RegexOptions.IgnoreCase));

        // The graded result: the summary names the session's questions and its accuracy, which is the
        // "receives a graded result" half of criterion 2 seen through the interface.
        var summary = await Page.ContentAsync();
        Assert.That(summary, Does.Contain("Accuracy").IgnoreCase,
            "the summary did not render a graded accuracy figure");
        Assert.That(summary, Does.Match(@"\d+\s*%"),
            "the summary rendered no percentage");
    }
}
