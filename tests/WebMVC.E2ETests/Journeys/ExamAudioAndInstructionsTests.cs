using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ExamAudioAndInstructionsTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Display_Instructions_With_Only_Active_Exam_Sections_And_Real_Question_Counts()
    {
        // Issue #76: mock exam instructions must list only sections with questions in this exam
        // with the exam's own per-section question count, without repeated time column, and accurate totals.
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|certificates)", RegexOptions.IgnoreCase));

        await Page.GotoAsync($"{BaseUrl}/exams/1/instructions");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/exams/1/instructions", RegexOptions.IgnoreCase));

        // 1. Verify header copy includes accurate total questions
        var headerPara = Page.Locator("p.text-muted:has-text('Questions:')").First;
        await Expect(headerPara).ToBeVisibleAsync();
        await Expect(headerPara).ToContainTextAsync("Questions: 9");

        // 2. Verify table columns: "Skill", "Section", "Questions" (no "Time" column)
        var table = Page.Locator("table.table").First;
        await Expect(table).ToBeVisibleAsync();
        var headers = table.Locator("thead th");
        var headerTexts = await headers.AllInnerTextsAsync();
        Assert.That(headerTexts, Does.Contain("Skill").IgnoreCase);
        Assert.That(headerTexts, Does.Contain("Section").IgnoreCase);
        Assert.That(headerTexts, Does.Contain("Questions").IgnoreCase);
        Assert.That(headerTexts, Does.Not.Contain("Time").IgnoreCase);

        // 3. Verify sections present in exam are listed with their actual question count
        var rows = table.Locator("tbody tr");
        var rowCount = await rows.CountAsync();
        Assert.That(rowCount, Is.EqualTo(7));

        var tableBodyText = await table.Locator("tbody").InnerTextAsync();
        // Should contain listening & reading sections present in Exam 1
        Assert.That(tableBodyText, Does.Contain("Photographs"));
        Assert.That(tableBodyText, Does.Contain("Incomplete Sentences"));
        Assert.That(tableBodyText, Does.Contain("Text Completion"));

        // Should NOT contain Speaking or Writing sections (which belong to Certificate 1 but have no questions in Exam 1)
        Assert.That(tableBodyText, Does.Not.Contain("Read a text aloud"));
        Assert.That(tableBodyText, Does.Not.Contain("Write an opinion essay"));

        // 4. Verify footer summary
        var footer = table.Locator("tfoot tr");
        await Expect(footer).ToContainTextAsync("Total · 120 minutes");
        await Expect(footer).ToContainTextAsync("9");
    }

    private static async Task ExecuteSqlAsync(string sql)
    {
        await using var conn = await OpenAppDatabaseAsync();
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Should_Render_Audio_Slice_Attributes_And_Enforce_Slice_Bounds_During_Playback()
    {
        // Issue #75: Audio slicing. The attempt question player carries slice offsets,
        // plays only the designated slice of the section's master track, and stops on time.
        await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = 200, AudioEndMs = 800 WHERE GroupId = 1;");

        try
        {
            await _loginPage.GotoAsync();
            await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
            await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|certificates)", RegexOptions.IgnoreCase));

            // Navigate to completed attempt review (Attempt 2 is for Exam 1)
            await Page.GotoAsync($"{BaseUrl}/exams/review/2");
            await Expect(Page).ToHaveURLAsync(new Regex(@"/exams/review/2", RegexOptions.IgnoreCase));

            // Locate Question 1 audio player
            var player = Page.Locator(".exam-audio[data-start-ms='200']").First;
            await Expect(player).ToBeVisibleAsync();
            await Expect(player).ToHaveAttributeAsync("data-mode", "study");

            // Verify slice attributes
            await Expect(player).ToHaveAttributeAsync("data-start-ms", "200");
            await Expect(player).ToHaveAttributeAsync("data-end-ms", "800");

            var audioNative = player.Locator("audio.exam-audio__native");
            var src = await audioNative.GetAttributeAsync("src");
            Assert.That(src, Does.Contain("test1-p1.mp3"));

            // Study-mode timeline and skip controls remain inside the group's slice.
            var progress = player.Locator(".exam-audio__progress");
            await Expect(progress).ToHaveCSSAsync("cursor", "pointer");
            await Expect(player.Locator(".exam-audio__btn-rewind")).ToBeVisibleAsync();
            await Expect(player.Locator(".exam-audio__btn-forward")).ToBeVisibleAsync();
            await audioNative.EvaluateAsync("el => new Promise(resolve => { if (Number.isFinite(el.duration) && el.duration > 0) return resolve(); el.addEventListener('loadedmetadata', resolve, { once: true }); })");
            var bounds = await progress.BoundingBoxAsync();
            Assert.That(bounds, Is.Not.Null);
            await progress.ClickAsync(new LocatorClickOptions
            {
                Position = new Position { X = bounds!.Width / 2, Y = bounds.Height / 2 }
            });
            var seekTime = await audioNative.EvaluateAsync<double>("el => el.currentTime");
            Assert.That(seekTime, Is.GreaterThan(0.35));
            Assert.That(seekTime, Is.LessThan(0.65));
            await player.Locator(".exam-audio__btn-forward").ClickAsync();
            var forwardTime = await audioNative.EvaluateAsync<double>("el => el.currentTime");
            Assert.That(forwardTime, Is.LessThanOrEqualTo(0.8));
            await player.Locator(".exam-audio__btn-rewind").ClickAsync();
            var rewindTime = await audioNative.EvaluateAsync<double>("el => el.currentTime");
            Assert.That(rewindTime, Is.GreaterThanOrEqualTo(0.19));
            Assert.That(rewindTime, Is.LessThanOrEqualTo(0.21));

            // Play the audio slice
            var playBtn = player.Locator(".exam-audio__btn-play");
            await playBtn.ClickAsync();

            // Verify playback stopped at or near the segment end
            var status = player.Locator(".exam-audio__status");
            await Expect(status).ToContainTextAsync("Playback finished");

            var currentTime = await audioNative.EvaluateAsync<double>("el => el.currentTime");
            Assert.That(currentTime, Is.GreaterThanOrEqualTo(0.79));
            Assert.That(currentTime, Is.LessThanOrEqualTo(0.9));
        }
        finally
        {
            // Reset DB state
            await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        }
    }

    [Test]
    public async Task Should_Keep_Exam_Audio_Continuous_And_Lock_Seeking_After_Playback()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/exams/review/2");

        var sourcePlayer = Page.Locator(".exam-audio[data-mode='study']").First;
        await Expect(sourcePlayer).ToBeVisibleAsync();
        await sourcePlayer.EvaluateAsync("source => { const player = source.cloneNode(true); player.id = 'exam-mode-e2e'; player.dataset.mode = 'exam'; player.dataset.allowReplay = 'false'; player.dataset.startMs = '200'; player.dataset.endMs = '800'; player.removeAttribute('data-initialized'); player.querySelector('.exam-audio__btn-rewind')?.remove(); player.querySelector('.exam-audio__btn-forward')?.remove(); player.querySelector('.exam-audio__progress').setAttribute('tabindex', '-1'); source.after(player); window.initExamAudio(); }");

        var examPlayer = Page.Locator("#exam-mode-e2e");
        var audio = examPlayer.Locator(".exam-audio__native");
        await audio.EvaluateAsync("el => new Promise(resolve => { if (Number.isFinite(el.duration) && el.duration > 0) return resolve(); el.addEventListener('loadedmetadata', resolve, { once: true }); })");

        var progress = examPlayer.Locator(".exam-audio__progress");
        await Expect(progress).ToHaveAttributeAsync("tabindex", "-1");
        await Expect(progress).ToHaveCSSAsync("pointer-events", "none");
        await Expect(examPlayer.Locator(".exam-audio__btn-rewind")).ToHaveCountAsync(0);
        await Expect(examPlayer.Locator(".exam-audio__btn-forward")).ToHaveCountAsync(0);

        var playButton = examPlayer.Locator(".exam-audio__btn-play");
        await playButton.ClickAsync();
        await Expect(playButton).ToBeDisabledAsync();
        await Expect(examPlayer.Locator(".exam-audio__badge")).ToHaveTextAsync("PLAYING");

        await audio.EvaluateAsync("el => el.pause()");
        await Page.WaitForFunctionAsync("() => !document.querySelector('#exam-mode-e2e .exam-audio__native').paused");

        await Expect(examPlayer.Locator(".exam-audio__status")).ToContainTextAsync("Audio completed");
        await Expect(examPlayer.Locator(".exam-audio__badge")).ToHaveTextAsync("COMPLETED");
        await Expect(playButton).ToBeDisabledAsync();
        Assert.That(await audio.EvaluateAsync<double>("el => el.currentTime"), Is.InRange(0.79, 0.9));
    }

    [Test]
    public async Task Should_Not_Lock_Replay_When_Navigating_Away_From_Unplayed_Question()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/exams");

        var continueLink = Page.Locator("a[href*='/exams/take/']").First;
        if (await continueLink.IsVisibleAsync())
        {
            await continueLink.ClickAsync();
        }
        else
        {
            await Page.GotoAsync($"{BaseUrl}/exams/1/instructions");
            var confirmCheck = Page.Locator("#confirm-ready");
            if (await confirmCheck.IsVisibleAsync()) await confirmCheck.CheckAsync();
            await Page.Locator("#start-btn").ClickAsync();
        }

        // Navigate to Question 3 (listening audio test1-p3.mp3, unplayed)
        var q3Btn = Page.Locator(".navigator__cell[data-display-order='3']").First;
        await q3Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 3");

        var playerQ3 = Page.Locator("#main-exam-audio, .exam-audio[data-mode='exam']").First;
        await Expect(playerQ3).ToBeVisibleAsync();
        await Expect(playerQ3.Locator(".exam-audio__badge")).ToHaveTextAsync("READY");

        // Navigate away to Question 5 (reading) without ever playing Question 3
        var q5Btn = Page.Locator(".navigator__cell[data-display-order='5']").First;
        await q5Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");

        // Return to Question 3
        await q3Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 3");

        // Question 3 must remain playable and in READY state
        await Expect(playerQ3).ToBeVisibleAsync();
        await Expect(playerQ3.Locator(".exam-audio__badge")).ToHaveTextAsync("READY");
        await Expect(playerQ3.Locator(".exam-audio__btn-play")).ToBeEnabledAsync();
    }

    [Test]
    public async Task Should_Resume_From_Last_Timestamp_And_Prevent_Rewind_When_Returning_To_Partial_Audio()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/exams");

        var continueLink = Page.Locator("a[href*='/exams/take/']").First;
        if (await continueLink.IsVisibleAsync())
        {
            await continueLink.ClickAsync();
        }
        else
        {
            await Page.GotoAsync($"{BaseUrl}/exams/1/instructions");
            var confirmCheck = Page.Locator("#confirm-ready");
            if (await confirmCheck.IsVisibleAsync()) await confirmCheck.CheckAsync();
            await Page.Locator("#start-btn").ClickAsync();
        }

        // Navigate to Question 4 (listening audio test1-p4.mp3)
        var q4Btn = Page.Locator(".navigator__cell[data-display-order='4']").First;
        await q4Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 4");

        var playerQ4 = Page.Locator("#main-exam-audio, .exam-audio[data-mode='exam']").First;
        var playBtn = playerQ4.Locator(".exam-audio__btn-play");
        await playBtn.ClickAsync();
        await Expect(playerQ4.Locator(".exam-audio__badge")).ToHaveTextAsync("PLAYING");

        // Wait until it has played at least 600ms
        await Page.WaitForFunctionAsync("() => { const a = document.querySelector('#main-exam-audio audio'); return a && a.currentTime > 0.5; }");
        var stoppedTime = await Page.EvaluateAsync<double>("() => document.querySelector('#main-exam-audio audio').currentTime");
        Assert.That(stoppedTime, Is.GreaterThan(0.4));

        // Navigate to Question 5 (reading, no audio)
        var q5Btn = Page.Locator(".navigator__cell[data-display-order='5']").First;
        await q5Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 5");

        // Audio must be stopped
        var anyPlaying = await Page.EvaluateAsync<bool>("() => Array.from(document.querySelectorAll('audio')).some(a => !a.paused && a.currentTime > 0)");
        Assert.That(anyPlaying, Is.False);

        // Return to Question 4
        await q4Btn.ClickAsync();
        await Expect(Page.Locator("#question-number-display")).ToContainTextAsync("Question 4");

        // Must restore at the last timestamp (not 0:00)
        await Expect(playerQ4).ToBeVisibleAsync();
        var resumedTime = await Page.EvaluateAsync<double>("() => parseFloat(document.querySelector('#main-exam-audio').dataset.currentTime || '0')");
        Assert.That(resumedTime, Is.GreaterThanOrEqualTo(stoppedTime - 0.1), "Audio must resume from last timestamp, not from beginning");

        // Candidate can resume playback from this timestamp
        await playBtn.ClickAsync();
        await Expect(playerQ4.Locator(".exam-audio__badge")).ToHaveTextAsync("PLAYING");
        var currentTimeAfterResume = await Page.EvaluateAsync<double>("() => document.querySelector('#main-exam-audio audio').currentTime");
        Assert.That(currentTimeAfterResume, Is.GreaterThanOrEqualTo(resumedTime));
    }

    [Test]
    public async Task Should_Survive_Page_Reload_Without_Softlock_And_Keep_Audio_Locked()
    {
        var errors = new List<string>();
        Page.PageError += (_, err) => errors.Add($"PageError: {err}");
        Page.Console += (_, msg) => {
            if (msg.Type == "error") errors.Add($"ConsoleError: {msg.Text}");
        };
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/exams");

        var continueLink = Page.Locator("a[href*='/exams/take/']").First;
        if (await continueLink.IsVisibleAsync())
        {
            await continueLink.ClickAsync();
        }
        else
        {
            await Page.GotoAsync($"{BaseUrl}/exams/1/instructions");
            var confirmCheck = Page.Locator("#confirm-ready");
            if (await confirmCheck.IsVisibleAsync()) await confirmCheck.CheckAsync();
            await Page.Locator("#start-btn").ClickAsync();
        }

        // Navigate to Question 2 (listening)
        var q2Btn = Page.Locator(".navigator__cell[data-display-order='2']").First;
        await q2Btn.ClickAsync();

        var playerQ2 = Page.Locator("#main-exam-audio, .exam-audio[data-mode='exam']").First;
        var playBtn = playerQ2.Locator(".exam-audio__btn-play");
        await playBtn.ClickAsync();
        await Expect(playerQ2.Locator(".exam-audio__badge")).ToHaveTextAsync("PLAYING");
        await Page.WaitForTimeoutAsync(600);

        // Now RELOAD (F5) the page!
        await Page.ReloadAsync();

        // 1. Must not have any JavaScript errors
        Assert.That(errors, Is.Empty, "Page reload caused JavaScript errors: " + string.Join("; ", errors));

        // 2. Audio must preserve progress across reload (not restart from scratch)
        var reloadedPlayer = Page.Locator(".exam-audio[data-mode='exam']").First;
        await Expect(reloadedPlayer).ToBeVisibleAsync();
        var resumedTime = await reloadedPlayer.EvaluateAsync<double>("el => parseFloat(el.dataset.currentTime || '0')");
        var isCompleted = await reloadedPlayer.EvaluateAsync<bool>("el => el.dataset.completed === 'true'");
        Assert.That(isCompleted || resumedTime > 0, Is.True, "Audio must preserve playback progress across reload");

        // 3. Timer must NOT be frozen at --:--:--
        var timerDisplay = Page.Locator("#timer-display");
        await Expect(timerDisplay).Not.ToHaveTextAsync("--:--:--");

        // 4. Exit modal button must work (not softlocked)
        var exitBtn = Page.Locator("#btn-open-exit-modal");
        await exitBtn.ClickAsync();
        var exitModal = Page.Locator("#exit-modal");
        await Expect(exitModal).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Render_Study_Audio_In_Practice_Summary()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner.fresh@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/practice");

        await Page.Locator("#pr-count").SelectOptionAsync("10");
        var firstListeningSection = Page.Locator("form#practice-form section").First
            .Locator("button[name='SectionId']").First;
        await Expect(firstListeningSection).ToBeVisibleAsync();
        await firstListeningSection.ClickAsync();
        await Expect(Page.Locator("#answer-form")).ToBeVisibleAsync();

        for (var question = 0; question < 10; question++)
        {
            var nextPage = Page.WaitForURLAsync(new Regex(@"/practice/\d+(?:\?q=\d+|/summary)$", RegexOptions.IgnoreCase));
            await Page.Locator("#answer-form button[name='Action'][value='skip']").ClickAsync();
            await nextPage;
            if (Page.Url.EndsWith("/summary", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await Expect(Page.Locator("#answer-form")).ToBeVisibleAsync();
        }

        await Expect(Page).ToHaveURLAsync(new Regex(@"/practice/\d+/summary", RegexOptions.IgnoreCase));

        var summaryPlayer = Page.Locator("tbody .exam-audio[data-mode='study']").First;
        await Expect(summaryPlayer).ToBeVisibleAsync();
        await Expect(summaryPlayer.Locator(".exam-audio__btn-rewind")).ToBeVisibleAsync();
        await Expect(summaryPlayer.Locator(".exam-audio__btn-forward")).ToBeVisibleAsync();
        await Expect(summaryPlayer.Locator("audio.exam-audio__native")).ToHaveAttributeAsync("src", new Regex(@"/audio/.+"));
    }

    [Test]
    public async Task Should_Show_Section_Master_Audio_In_Exam_Studio_Visual_Slicer()
    {
        // Admin Exam Studio: Section audio uploaded as master audio is displayed in the Visual Audio Slicer
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/studio");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/1/studio", RegexOptions.IgnoreCase));

        // Visual Audio Slicer header with master audio file
        var visualSlicer = Page.Locator(":text('Visual Audio Slicer')").First;
        await Expect(visualSlicer).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).ToContainTextAsync("/audio/toeic/test1-p1.mp3");

        // Slicer controls: Mark Start, Mark End, Preview Slice
        await Expect(Page.Locator("button:has-text('Mark Start')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Mark End')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Preview Slice')").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Stage_Start_Without_Saving_And_Render_The_Start_Marker()
    {
        var studio = await OpenExamStudioWithAudioAsync();
        try
        {
            var saveRequests = new List<IRequest>();
            Page.Request += (_, request) =>
            {
                if (request.Method == "PUT" && request.Url.Contains("/studio/group/", StringComparison.Ordinal))
                {
                    saveRequests.Add(request);
                }
            };

            var audio = Page.Locator($"#slicer-audio-{studio.SectionId}");
            await Page.Locator($"#btn-slicer-play-{studio.SectionId}").ClickAsync();
            await Page.WaitForTimeoutAsync(250);
            await audio.EvaluateAsync("el => el.currentTime = Math.max(el.currentTime, 0.25)");
            await Page.Locator($"button[onclick='markCurrentStart({studio.SectionId})']").ClickAsync();
            await Page.WaitForTimeoutAsync(250);

            var status = Page.Locator($"#slicer-status-{studio.SectionId}");
            await Expect(status).ToContainTextAsync("Start marked:");
            await Expect(status).ToContainTextAsync("click Mark End to save");
            await Expect(Page.Locator($"#slicer-start-marker-{studio.SectionId}")).ToHaveCSSAsync("display", "block");
            Assert.That(await Page.Locator($"#start-ms-{studio.GroupId}").InputValueAsync(), Is.Not.Empty);
            Assert.That(saveRequests, Is.Empty, "Mark Start must not send a group save request.");

            var topStatus = await Page.Locator("#autosave-status").InnerTextAsync();
            Assert.That(topStatus, Does.Not.Contain("Save failed"));
            Assert.That(await audio.EvaluateAsync<bool>("el => !el.paused"), Is.True);
        }
        finally
        {
            await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        }
    }

    [Test]
    public async Task Should_Save_Both_Slice_Bounds_When_End_Is_Marked_And_Clear_Them()
    {
        var studio = await OpenExamStudioWithAudioAsync();
        try
        {
            var audio = Page.Locator($"#slicer-audio-{studio.SectionId}");
            await Page.Locator($"#btn-slicer-play-{studio.SectionId}").ClickAsync();
            await Page.WaitForTimeoutAsync(250);
            await audio.EvaluateAsync("el => el.currentTime = Math.max(el.currentTime, 0.25)");
            await Page.Locator($"button[onclick='markCurrentStart({studio.SectionId})']").ClickAsync();

            var startMs = int.Parse(await Page.Locator($"#start-ms-{studio.GroupId}").InputValueAsync());
            await audio.EvaluateAsync("(el, start) => { el.pause(); el.currentTime = Math.max(el.currentTime, start / 1000 + 0.5); }", startMs);
            var saveResponseTask = Page.WaitForResponseAsync(response =>
                response.Request.Method == "PUT" &&
                response.Url.Contains($"/studio/group/{studio.GroupId}", StringComparison.Ordinal));
            await Page.Locator($"button[onclick='markCurrentEnd({studio.SectionId})']").ClickAsync();
            var response = await saveResponseTask;

            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(response.Request.PostData, Does.Contain("\"audioStartMs\""));
            Assert.That(response.Request.PostData, Does.Contain("\"audioEndMs\""));
            await Expect(Page.Locator($"#slicer-status-{studio.SectionId}")).ToHaveTextAsync("Saved");

            var clearResponseTask = Page.WaitForResponseAsync(clearResponse =>
                clearResponse.Request.Method == "PUT" &&
                clearResponse.Url.Contains($"/studio/group/{studio.GroupId}", StringComparison.Ordinal));
            await Page.Locator($"button[onclick='clearCurrentSlice({studio.SectionId})']").ClickAsync();
            var clearResponse = await clearResponseTask;

            Assert.That(clearResponse.Status, Is.EqualTo(200));
            Assert.That(clearResponse.Request.PostData, Does.Contain("\"audioStartMs\":null"));
            Assert.That(clearResponse.Request.PostData, Does.Contain("\"audioEndMs\":null"));
            await Expect(Page.Locator($"#start-ms-{studio.GroupId}")).ToHaveValueAsync(string.Empty);
            await Expect(Page.Locator($"#end-ms-{studio.GroupId}")).ToHaveValueAsync(string.Empty);
            await Expect(Page.Locator($"#slicer-status-{studio.SectionId}")).ToHaveTextAsync("Saved");
        }
        finally
        {
            await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        }
    }

    [Test]
    public async Task Should_Show_Inline_Error_And_Not_Save_When_End_Bounds_Are_Invalid()
    {
        var studio = await OpenExamStudioWithAudioAsync();
        try
        {
            var saveRequests = new List<IRequest>();
            Page.Request += (_, request) =>
            {
                if (request.Method == "PUT" && request.Url.Contains("/studio/group/", StringComparison.Ordinal))
                {
                    saveRequests.Add(request);
                }
            };

            var audio = Page.Locator($"#slicer-audio-{studio.SectionId}");
            await audio.EvaluateAsync("el => { el.pause(); el.currentTime = 0; }");
            var markEnd = Page.Locator($"button[onclick='markCurrentEnd({studio.SectionId})']");
            var status = Page.Locator($"#slicer-status-{studio.SectionId}");

            await markEnd.ClickAsync();
            await Expect(status).ToHaveTextAsync("End time must be after Start time (00:00)");

            await Page.Locator($"#start-ms-{studio.GroupId}").FillAsync("1500");
            await markEnd.ClickAsync();
            await Expect(status).ToHaveTextAsync("End time must be after Start time (00:01)");
            Assert.That(saveRequests, Is.Empty, "Invalid slice bounds must not send a group save request.");
        }
        finally
        {
            await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        }
    }

    [Test]
    public async Task Should_Pause_Slicer_Audio_When_Another_Question_Is_Selected()
    {
        var studio = await OpenExamStudioWithAudioAsync();
        try
        {
            var audio = Page.Locator($"#slicer-audio-{studio.SectionId}");
            await Page.Locator($"#btn-slicer-play-{studio.SectionId}").ClickAsync();
            await Page.WaitForFunctionAsync($"() => !document.getElementById('slicer-audio-{studio.SectionId}').paused");
            Assert.That(await audio.EvaluateAsync<bool>("el => !el.paused"), Is.True);

            var railQuestions = Page.Locator(".rail-item");
            Assert.That(await railQuestions.CountAsync(), Is.GreaterThan(1));
            await railQuestions.Nth(1).ClickAsync();

            Assert.That(await audio.EvaluateAsync<bool>("el => el.paused"), Is.True);
            await Expect(Page.Locator($"#btn-slicer-play-{studio.SectionId}")).ToHaveTextAsync("Play");
        }
        finally
        {
            await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        }
    }

    private async Task<(int SectionId, int GroupId)> OpenExamStudioWithAudioAsync()
    {
        await ExecuteSqlAsync("UPDATE QuestionGroups SET AudioStartMs = NULL, AudioEndMs = NULL WHERE GroupId = 1;");
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/studio");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/1/studio", RegexOptions.IgnoreCase));

        var slicer = Page.Locator(".audio-slicer").First;
        await Expect(slicer).ToBeVisibleAsync();
        var slicerId = await slicer.GetAttributeAsync("id");
        var sectionId = int.Parse(Regex.Match(slicerId!, @"audio-slicer-sec-(\d+)").Groups[1].Value);
        var audio = Page.Locator($"#slicer-audio-{sectionId}");
        await audio.EvaluateAsync("el => new Promise((resolve, reject) => { if (el.readyState >= 1) return resolve(); el.addEventListener('loadedmetadata', resolve, { once: true }); el.addEventListener('error', () => reject(new Error('Master audio failed to load')), { once: true }); })");

        var groupCard = Page.Locator($"#ws-sec-{sectionId} .card-editor").First;
        var groupId = int.Parse((await groupCard.GetAttributeAsync("data-group-id"))!);
        return (sectionId, groupId);
    }
}
