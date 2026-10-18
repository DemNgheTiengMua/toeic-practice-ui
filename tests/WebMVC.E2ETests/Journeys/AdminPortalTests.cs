using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminPortalTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Allow_Admin_Access_To_Management_Dashboard()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/Admin");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Admin", RegexOptions.IgnoreCase));

        // Verify Admin Header and Title
        await Expect(Page.Locator("h1:has-text('Dashboard'), strong:has-text('extra efficient Admin')").First).ToBeVisibleAsync();

        // Verify admin sections exist
        await Expect(Page.Locator(":text('User accounts'), :text('Users')").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Not_Show_Question_Import_In_Admin_Sidebar_Navigation()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/exams");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams", RegexOptions.IgnoreCase));

        // Verify Question import link is completely removed from sidebar
        await Expect(Page.Locator(".app-sidebar a[href='/admin/imports']")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".app-sidebar:has-text('Question import')")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Should_Show_Import_Exam_Modal_On_Admin_Exams_Page()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/exams");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams", RegexOptions.IgnoreCase));

        // Verify button exists in header action area
        var importBtn = Page.Locator("button:has-text('Import Exam (.zip / .json)')");
        await Expect(importBtn.First).ToBeVisibleAsync();

        var dialog = Page.Locator("#import-exam-dialog");
        await Expect(dialog).ToBeHiddenAsync();

        // Click to open modal
        await importBtn.First.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();

        // Verify form fields inside modal
        await Expect(dialog.Locator("#import-certId")).ToBeVisibleAsync();
        await Expect(dialog.Locator("#import-file")).ToBeVisibleAsync();
        await Expect(dialog.Locator("button[type='submit']:has-text('Validate and import')")).ToBeVisibleAsync();

        // Close via close button
        await dialog.Locator(".modal__close").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Test]
    public async Task Should_Redirect_Legacy_Imports_Route_To_Admin_Exams()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/imports");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams\?import=true", RegexOptions.IgnoreCase));

        // Modal should automatically be open
        var dialog = Page.Locator("#import-exam-dialog");
        await Expect(dialog).ToBeVisibleAsync();
    }
}
