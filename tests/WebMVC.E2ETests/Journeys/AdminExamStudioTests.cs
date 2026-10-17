using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminExamStudioTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Navigate_To_Exam_Studio_And_Verify_Curriculum_Rail()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/studio");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/\d+/studio", RegexOptions.IgnoreCase));

        // Verify the 3-panel layout: Left Rail, Center Workstation, Right Audit
        await Expect(Page.Locator("#studio-left-rail")).ToBeVisibleAsync();
        await Expect(Page.Locator("#studio-workstation")).ToBeVisibleAsync();
        await Expect(Page.Locator("#studio-right-audit")).ToBeVisibleAsync();

        // Verify rail items and question counters
        await Expect(Page.Locator("#rail-total-count")).ToBeVisibleAsync();
        var railSections = Page.Locator(".rail-section");
        var sectionCount = await railSections.CountAsync();
        Assert.That(sectionCount, Is.GreaterThan(0), "Exam Studio should render at least one section in the curriculum rail.");
    }

    [Test]
    public async Task Should_Open_Quick_Ingest_Modal_Load_Sample_And_Parse_Staging_Preview()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/studio");

        // Trigger Quick Ingest modal
        await Page.Locator("#btn-studio-quick-ingest").ClickAsync();
        var modal = Page.Locator("#quick-ingest-modal");
        await Expect(modal).ToBeVisibleAsync();

        // Load sample template into scratchpad
        await Page.Locator("#qi-sample-btn").ClickAsync();
        var rawText = await Page.Locator("#qi-raw-text").InputValueAsync();
        Assert.That(rawText, Is.Not.Empty, "Sample template button should populate raw text textarea.");

        // Pre-flight staging parse
        await Page.Locator("#qi-parse-btn").ClickAsync();
        var previewContainer = Page.Locator("#qi-preview-container");
        await Expect(previewContainer).ToBeVisibleAsync();

        var previewRows = Page.Locator("#qi-preview-body tr");
        var rowCount = await previewRows.CountAsync();
        Assert.That(rowCount, Is.GreaterThan(0), "Pre-flight staging preview should parse and display question rows.");

        // Close modal
        await Page.Locator("#quick-ingest-close").ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Verify_Readiness_Audit_Placard_And_Export_Links()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/studio");

        // Check readiness audit panel elements
        await Expect(Page.Locator("#audit-status-badge")).ToBeVisibleAsync();
        await Expect(Page.Locator("#audit-q-count")).ToBeVisibleAsync();

        // Check dual export buttons
        var exportZipBtn = Page.Locator("#btn-export-exam");
        await Expect(exportZipBtn).ToBeVisibleAsync();
        var zipHref = await exportZipBtn.GetAttributeAsync("href");
        Assert.That(zipHref, Does.Contain("/export?format=zip"));

        var exportJsonBtn = Page.Locator("#btn-export-json");
        await Expect(exportJsonBtn).ToBeVisibleAsync();
        var jsonHref = await exportJsonBtn.GetAttributeAsync("href");
        Assert.That(jsonHref, Does.Contain("/export?format=json"));
    }
}
