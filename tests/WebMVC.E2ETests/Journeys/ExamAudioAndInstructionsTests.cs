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

            // Verify slice attributes
            await Expect(player).ToHaveAttributeAsync("data-start-ms", "200");
            await Expect(player).ToHaveAttributeAsync("data-end-ms", "800");

            var audioNative = player.Locator("audio.exam-audio__native");
            var src = await audioNative.GetAttributeAsync("src");
            Assert.That(src, Does.Contain("test1-p1.mp3"));

            // Play the audio slice
            var playBtn = player.Locator(".exam-audio__btn-play");
            await playBtn.ClickAsync();

            // Wait for segment playback to conclude (600ms slice)
            await Page.WaitForTimeoutAsync(1000);

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
}
