using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// Behavioural regression coverage for the sitting-room and Practice QoL defects in
/// docs/specs/sitting-room-navigation-and-practice-qol-defects.md. These drive the real browser
/// rather than asserting on view source, so they fail when the behaviour regresses even if the
/// markup happens to keep its shape.
/// </summary>
/// <remarks>
/// Not parallelizable: the setup abandons in-progress attempts on exam 1 and tops up the demo
/// learner's credit ledger, and both are shared mutable state — running these alongside the other
/// journeys made them race (the palette/token test failed intermittently).
/// </remarks>
[NonParallelizable]
[TestFixture]
public class SittingAndPracticeQolTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Advance_From_Listening_To_Reading_On_The_Last_Question_Of_The_Skill()
    {
        var attemptId = await StartToeicListeningAttemptAsync();

        await NavigateForwardToQuestionAsync(4);

        var nextButton = Page.Locator("#btn-next");
        await Expect(nextButton).ToBeEnabledAsync();
        await Expect(nextButton).ToContainTextAsync("Finish Listening & start Reading");

        await nextButton.ClickAsync();

        // The Reading phase must actually load, not sit behind an expired clock.
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");
        await Expect(Page).ToHaveURLAsync(new Regex($@"/exams/take/{attemptId}\?q=5"));
        await Expect(Page.Locator("#topbar-section-badge")).ToContainTextAsync("Reading");
    }

    [Test]
    public async Task Should_Keep_The_Attempt_Clock_Intact_Across_A_Phase_Advance()
    {
        await StartToeicListeningAttemptAsync();
        await NavigateForwardToQuestionAsync(4);

        var timer = Page.Locator("#timer-display");
        var before = ParseTimer(await timer.InnerTextAsync());

        await Page.Locator("#btn-next").ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");

        var after = ParseTimer(await timer.InnerTextAsync());

        // Advancing skills must not shrink the total attempt time down to the new phase's length.
        Assert.That(Math.Abs(before - after), Is.LessThan(120),
            $"The attempt clock collapsed from {before}s to {after}s when the phase advanced.");
    }

    [Test]
    public async Task Should_Not_Rewind_The_Phase_Back_When_The_Timer_Ticks()
    {
        await StartToeicListeningAttemptAsync();
        await NavigateForwardToQuestionAsync(4);
        await Page.Locator("#btn-next").ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");

        // Several 1-second ticks used to snap the sitting room back to the first skill.
        await Page.WaitForTimeoutAsync(3500);

        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");
        var activeSkill = await Page.EvaluateAsync<int>("() => window.ExamSitting.activePhaseIndex");
        Assert.That(activeSkill, Is.EqualTo(1), "The active phase must not be recomputed from the wall clock");
    }

    [Test]
    public async Task Should_Expose_An_Accessible_Reason_On_Locked_Palette_Chips()
    {
        await StartToeicListeningAttemptAsync();

        // calm.css hides the bottom palette at >=1025px because the sidebar already acts as the
        // navigator there, so this accessibility contract is only observable on a narrow viewport.
        await Page.SetViewportSizeAsync(900, 900);
        await Page.ReloadAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 1");

        var lockedChip = Page.Locator("#bottom-chip-5");
        await Expect(lockedChip).ToBeAttachedAsync();
        Assert.That(await lockedChip.GetAttributeAsync("aria-disabled"), Is.EqualTo("true"));
        Assert.That(await lockedChip.GetAttributeAsync("aria-describedby"), Is.EqualTo("palette-locked-reason"));
        Assert.That(await lockedChip.GetAttributeAsync("class"), Does.Contain("palette-chip--locked"));

        // ...and the chip must stay visible (it used to be hidden outright by the locked style).
        await Expect(lockedChip).ToBeVisibleAsync();
        await Expect(Page.Locator(".palette-legend")).ToContainTextAsync("Locked");
    }

    [Test]
    public async Task Should_Tokenize_A_Question_Stem_So_The_Highlighter_Can_Address_Words()
    {
        await StartToeicListeningAttemptAsync();

        // Annotation tools are enabled on the Reading Comprehension section, which starts at Q9
        // on this exam; the earlier Reading sections carry no tools config.
        await NavigateForwardToQuestionAsync(4);
        await Page.Locator("#btn-next").ClickAsync();
        await Expect(Page.Locator("#topbar-section-badge")).ToContainTextAsync("Reading");
        await NavigateForwardToQuestionAsync(9);
        await Expect(Page.Locator("#topbar-section-badge")).ToContainTextAsync("Reading Comprehension");

        var stem = Page.Locator("#question-stem-display");
        var tokenCount = await stem.Locator(".ee-token").CountAsync();
        Assert.That(tokenCount, Is.GreaterThan(5), "The question stem must be word-tokenized");

        // Selecting tokens must surface the floating toolbar. Read back what the handler decided
        // so a failure says whether the gate (tools config) or the selection was the problem.
        var diagnosis = await Page.EvaluateAsync<string>(@"() => {
            const stem = document.getElementById('question-stem-display');
            const tokens = stem.querySelectorAll('.ee-token');
            const range = document.createRange();
            range.setStart(tokens[1].firstChild, 0);
            range.setEnd(tokens[3].firstChild, tokens[3].firstChild.length);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
            const q = window.ExamSitting.questionMap[window.ExamSitting.currentDisplayOrder];
            return JSON.stringify({
                tokenCount: tokens.length,
                rawCfg: q ? q.toolsConfigJson : 'NO_QUESTION',
                parsedCfg: q ? window.ExamSitting.parseToolsConfig(q.toolsConfigJson) : null,
                display: document.getElementById('highlighter-popover').style.display,
                selected: sel.toString()
            });
        }");

        Assert.That(await Page.Locator("#highlighter-popover").IsVisibleAsync(), Is.True,
            $"The highlighter toolbar did not open. Diagnosis: {diagnosis}");
    }

    [Test]
    public async Task Should_Reveal_A_Practice_Hint_And_ReEnable_The_Control_After_Navigating()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        await SwitchToToeicAsync();
        await OpenPracticeSectionAsync("P3");

        var hintButton = Page.Locator("#show-hint-btn");
        await Expect(hintButton).ToBeEnabledAsync();
        await hintButton.ClickAsync();

        var hintBox = Page.Locator("#hint-box");
        await Expect(hintBox).ToBeVisibleAsync();
        Assert.That((await hintBox.InnerTextAsync()).Trim(), Is.Not.Empty, "The hint text must actually render");

        // Answer, move on, and come back — the control must not stay disabled.
        await Page.Locator(".option").First.ClickAsync();
        await Page.Locator("#submit-btn").ClickAsync();
        await Expect(Page.Locator("#submit-btn")).ToContainTextAsync("Next question", new() { Timeout = 10000 });
        await Page.Locator("#submit-btn").ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 2");

        var enabled = await Page.EvaluateAsync<bool>("() => { const b = document.getElementById('show-hint-btn'); return b ? !b.disabled : false; }");
        Assert.That(enabled, Is.True, "The hint control must be usable again on the next question");
    }

    [Test]
    public async Task Should_Keep_Practice_Filters_Inside_The_Sidebar_Card()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        await SwitchToToeicAsync();
        await OpenPracticeSectionAsync("P3");

        var overflow = await Page.EvaluateAsync<bool>(@"() => {
            const saved = document.getElementById('filter-saved');
            if (!saved) return true;
            return saved.getBoundingClientRect().right > window.innerWidth + 1;
        }");

        Assert.That(overflow, Is.False, "The Saved filter pill must not be clipped past the viewport edge");

        var pageScroll = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(pageScroll, Is.LessThanOrEqualTo(1), "The practice page must not scroll horizontally");
    }

    private static double ParseTimer(string text)
    {
        var parts = text.Trim().Split(':');
        Assert.That(parts.Length, Is.EqualTo(3), $"Unexpected timer format '{text}'");
        return (int.Parse(parts[0]) * 3600) + (int.Parse(parts[1]) * 60) + int.Parse(parts[2]);
    }

    private async Task SwitchToToeicAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/certificates");
        var switchButton = Page.GetByRole(AriaRole.Button, new() { Name = "Switch to TOEIC", Exact = true });
        if (await switchButton.CountAsync() > 0)
        {
            await switchButton.First.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }

    private async Task OpenPracticeSectionAsync(string sectionCode)
    {
        await Page.GotoAsync($"{BaseUrl}/practice");
        var card = Page.Locator($"[data-section-code='{sectionCode}'], .practice-card:has-text('{sectionCode}')").First;
        if (await card.CountAsync() == 0)
        {
            card = Page.Locator($"text={sectionCode}").First;
        }

        await card.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/practice/\d+"), new() { Timeout = 15000 });
    }

    private async Task NavigateForwardToQuestionAsync(int targetOrder)
    {
        var questionNumber = Page.Locator("#question-number-display");
        await Expect(questionNumber).ToBeVisibleAsync();

        var text = await questionNumber.InnerTextAsync();
        var match = Regex.Match(text, @"(?<order>\d+)");
        Assert.That(match.Success, Is.True, $"Could not read the current question from '{text}'.");
        var current = int.Parse(match.Groups["order"].Value);
        Assert.That(targetOrder, Is.GreaterThanOrEqualTo(current),
            $"Cannot navigate forward to Question {targetOrder} from Question {current}.");

        for (var order = current; order < targetOrder; order++)
        {
            await Expect(questionNumber).ToContainTextAsync($"Question {order}");
            var nextButton = Page.Locator("#btn-next");
            await Expect(nextButton).ToBeEnabledAsync();
            await nextButton.ClickAsync();
        }

        await Expect(questionNumber).ToContainTextAsync($"Question {targetOrder}");
    }

    private async Task<int> StartToeicListeningAttemptAsync()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        await SwitchToToeicAsync();

        // Abandon any attempt already in progress on this exam so the run starts at Question 1
        // instead of resuming mid-exam (the sitting room renders the server's current question),
        // and top the wallet up so repeated local runs never fail on a depleted ledger.
        await AbandonInProgressAttemptsAsync(1);
        await TopUpLearnerCreditsAsync();

        await Page.GotoAsync($"{BaseUrl}/exams/1/instructions");
        var confirmCheck = Page.Locator("#confirm-ready");
        if (await confirmCheck.CountAsync() > 0)
        {
            await confirmCheck.CheckAsync();
        }

        var startButton = Page.Locator("#start-btn");
        await Expect(startButton).ToBeEnabledAsync(new() { Timeout = 15000 });
        await startButton.ClickAsync();

        // Start lands on the pre-flight gate or, for an exam without audio, straight in the sitting
        // room. Wait for one of them to render: the URL commits before the body is parsed, so a
        // non-waiting CountAsync() here used to see no button on a cold first run, skip the click
        // and time out below waiting for /exams/take (F2).
        var enterTest = Page.Locator("#start-test-btn");
        var questionNumber = Page.Locator("#question-number-display");
        await Expect(enterTest.Or(questionNumber).First).ToBeVisibleAsync(new() { Timeout = 20000 });

        if (await enterTest.IsVisibleAsync())
        {
            // Enabled once the audio tracks have pre-buffered.
            await Expect(enterTest).ToBeEnabledAsync(new() { Timeout = 30000 });
            await enterTest.ClickAsync();

            // A refused verification redirects back to the gate with its reason on a placard;
            // fail with that reason instead of a bare navigation timeout.
            var refusal = Page.Locator("#preflight-error");
            await Expect(questionNumber.Or(refusal).First).ToBeVisibleAsync(new() { Timeout = 30000 });
            if (await refusal.IsVisibleAsync())
            {
                Assert.Fail($"Pre-flight verification was refused: {await refusal.InnerTextAsync()}");
            }
        }

        await Expect(Page).ToHaveURLAsync(new Regex(@"/exams/take/(?<id>\d+)"));
        await Expect(questionNumber).ToContainTextAsync("Question 1");

        var match = Regex.Match(Page.Url, @"/exams/take/(?<id>\d+)");
        Assert.That(match.Success, Is.True, "The sitting room URL must carry the attempt id");
        return int.Parse(match.Groups["id"].Value);
    }

    private static async Task AbandonInProgressAttemptsAsync(int examId)
    {
        await using var connection = await OpenAppDatabaseAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ExamAttempts
            SET Status = 'abandoned'
            WHERE ExamId = @examId AND Status = 'in_progress';
            """;
        command.Parameters.AddWithValue("@examId", examId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Keeps the demo learner solvent so a local E2E run does not fail once the seeded 8 credits
    /// are spent by earlier attempts. The ledger stays append-only: this appends an admin grant,
    /// which is the only positive reason the schema allows without an originating order.
    /// </summary>
    private static async Task TopUpLearnerCreditsAsync()
    {
        await using var connection = await OpenAppDatabaseAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @adminId INT = (SELECT TOP (1) UserId FROM Users WHERE Email = 'admin@demo.local');
            DECLARE @learnerId INT = (SELECT TOP (1) UserId FROM Users WHERE Email = 'learner@demo.local');
            IF @adminId IS NOT NULL AND @learnerId IS NOT NULL
               AND (SELECT ISNULL(SUM(Delta), 0) FROM CreditTransactions WHERE UserId = @learnerId) < 5
            BEGIN
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note, CreatedAt)
                VALUES (@learnerId, 'admin_grant', 20, @adminId, 'E2E sitting test top-up', SYSDATETIMEOFFSET());
            END
            """;
        await command.ExecuteNonQueryAsync();
    }
}
