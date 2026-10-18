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

    [Test]
    public async Task Should_Redirect_Legacy_Questions_Route_Directly_To_Exam_Studio()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate directly to legacy /admin/exams/1/questions with query params
        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/questions?sectionId=1");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/1/studio\?sectionId=1", RegexOptions.IgnoreCase));

        // Verify the 3-panel layout renders cleanly and no 500 occurred
        await Expect(_studioPage.LeftRail).ToBeVisibleAsync();
        await Expect(_studioPage.CenterWorkstation).ToBeVisibleAsync();
        await Expect(_studioPage.RightAudit).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Render_Admin_Sidebar_And_Layout_On_Users_And_Orders_Pages()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate to /admin/users
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Expect(Page.Locator(".app-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("nav[aria-label='Admin navigation']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".nav-list__item[href='/admin/users']")).ToHaveClassAsync(new Regex(@"is-active"));

        // Navigate to /admin/orders
        await Page.GotoAsync($"{BaseUrl}/admin/orders");
        await Expect(Page.Locator(".app-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("nav[aria-label='Admin navigation']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".nav-list__item[href='/admin/orders']")).ToHaveClassAsync(new Regex(@"is-active"));
    }

    [Test]
    public async Task Should_Navigate_To_Exams_List_Filter_By_Cert_And_Purpose_And_Verify_Actions()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate to /admin/exams
        await Page.GotoAsync($"{BaseUrl}/admin/exams");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams", RegexOptions.IgnoreCase));

        // Verify Certificate and Purpose filter dropdowns exist
        var certSelect = Page.Locator("#f-cert");
        var purposeSelect = Page.Locator("#f-purpose");
        await Expect(certSelect).ToBeVisibleAsync();
        await Expect(purposeSelect).ToBeVisibleAsync();

        // Verify streamlined action buttons exist in exam table rows
        var studioButtons = Page.Locator("a.btn--primary:has-text('Studio')");
        await Expect(studioButtons.First).ToBeVisibleAsync();

        var editInfoButtons = Page.Locator("a:has-text('Edit Info')");
        await Expect(editInfoButtons.First).ToBeVisibleAsync();

        // Filter by purpose = mock
        await purposeSelect.SelectOptionAsync(new SelectOptionValue { Value = "mock" });
        await Page.Locator("form.toolbar button[type='submit']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"purpose=mock", RegexOptions.IgnoreCase));

        // Verify filtered table results display with Studio and Edit Info
        await Expect(Page.Locator("table.table")).ToBeVisibleAsync();
        await Expect(Page.Locator("a.btn--primary:has-text('Studio')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("a:has-text('Edit Info')").First).ToBeVisibleAsync();
    }
}
