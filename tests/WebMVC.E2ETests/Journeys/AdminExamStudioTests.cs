using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminExamStudioTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;
    private ExamStudioPage _studioPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
        _studioPage = new ExamStudioPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Navigate_To_Exam_Studio_And_Verify_Curriculum_Rail()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/\d+/studio", RegexOptions.IgnoreCase));

        // Verify the 3-panel layout: Left Rail, Center Workstation, Right Audit
        await Expect(_studioPage.LeftRail).ToBeVisibleAsync();
        await Expect(_studioPage.CenterWorkstation).ToBeVisibleAsync();
        await Expect(_studioPage.RightAudit).ToBeVisibleAsync();

        // Verify rail items and question counters
        await Expect(_studioPage.RailTotalCount).ToBeVisibleAsync();
        var sectionCount = await _studioPage.RailSections.CountAsync();
        Assert.That(sectionCount, Is.GreaterThan(0), "Exam Studio should render at least one section in the curriculum rail.");
    }

    [Test]
    public async Task Should_Open_Quick_Ingest_Modal_Load_Sample_And_Parse_Staging_Preview()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);

        // Trigger Quick Ingest modal
        await _studioPage.QuickIngestButton.ClickAsync();
        await Expect(_studioPage.QuickIngestModal).ToBeVisibleAsync();

        // Load sample template into scratchpad
        await _studioPage.QuickIngestSampleButton.ClickAsync();
        var rawText = await _studioPage.QuickIngestRawTextArea.InputValueAsync();
        Assert.That(rawText, Is.Not.Empty, "Sample template button should populate raw text textarea.");

        // Pre-flight staging parse
        await _studioPage.QuickIngestParseButton.ClickAsync();
        await Expect(_studioPage.QuickIngestPreviewContainer).ToBeVisibleAsync();

        var rowCount = await _studioPage.QuickIngestPreviewRows.CountAsync();
        Assert.That(rowCount, Is.GreaterThan(0), "Pre-flight staging preview should parse and display question rows.");

        // Close modal
        await _studioPage.QuickIngestCloseButton.ClickAsync();
        await Expect(_studioPage.QuickIngestModal).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Verify_Readiness_Audit_Placard_And_Export_Links()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);

        // Check readiness audit panel elements
        await Expect(_studioPage.AuditStatusBadge).ToBeVisibleAsync();
        await Expect(_studioPage.AuditQuestionCount).ToBeVisibleAsync();

        // Check dual export buttons
        await Expect(_studioPage.ExportZipButton).ToBeVisibleAsync();
        var zipHref = await _studioPage.ExportZipButton.GetAttributeAsync("href");
        Assert.That(zipHref, Does.Contain("/export?format=zip"));

        await Expect(_studioPage.ExportJsonButton).ToBeVisibleAsync();
        var jsonHref = await _studioPage.ExportJsonButton.GetAttributeAsync("href");
        Assert.That(jsonHref, Does.Contain("/export?format=json"));
    }
}
