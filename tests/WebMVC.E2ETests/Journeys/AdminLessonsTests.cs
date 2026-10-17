using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminLessonsTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_Navigate_To_Admin_Lessons_And_View_Listing()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/lessons");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/lessons", RegexOptions.IgnoreCase));

        // Expect title and toolbar
        await Expect(Page.Locator("h1:has-text('Guided Lessons')")).ToBeVisibleAsync();
        await Expect(Page.Locator("a:has-text('+ New Lesson')")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Import Manifest / Package')")).ToBeVisibleAsync();
        await Expect(Page.Locator("a:has-text('Download Template')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Create_New_Lesson_With_Cards_And_Redirect()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/lessons/create");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/lessons/create", RegexOptions.IgnoreCase));

        var lessonTitle = $"E2E Test Lesson {Guid.NewGuid().ToString("N")[..6]}";

        // Fill Lesson Metadata
        await Page.Locator("#Title").FillAsync(lessonTitle);
        await Page.Locator("#SkillId").FillAsync("2"); // Reading
        await Page.Locator("#EstimatedMinutes").FillAsync("6");
        await Page.Locator("#Summary").FillAsync("E2E Automated Test Lesson Deck");

        // Fill Initial Card
        await Page.Locator("input[name='Cards[0].Title']").FillAsync("Core Principle");
        await Page.Locator("textarea[name='Cards[0].ContentMarkdown']").FillAsync("### Rule Summary\n\nAlways verify subject-verb agreement.");

        // Add a Micro-check card
        await Page.Locator("button:has-text('+ Micro-Check')").ClickAsync();

        // Fill Micro-Check Card (#2, index 1)
        await Page.Locator("input[name='Cards[1].Title']").FillAsync("Comprehension Check");
        await Page.Locator("textarea[name='Cards[1].ContentMarkdown']").FillAsync("Choose the best option to complete the statement.");
        await Page.Locator("textarea[name='Cards[1].QuestionPrompt']").FillAsync("The manager _______ the financial results today.");
        await Page.Locator("input[name='Cards[1].OptionA']").FillAsync("announces");
        await Page.Locator("input[name='Cards[1].OptionB']").FillAsync("is announcing");
        await Page.Locator("select[name='Cards[1].CorrectOption']").SelectOptionAsync("B");
        await Page.Locator("textarea[name='Cards[1].Explanation']").FillAsync("The prompt indicates an action happening today.");

        // Submit form
        await Page.Locator("button:has-text('Save Lesson')").ClickAsync();

        // Verify redirect to listing and presence of new lesson
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/lessons", RegexOptions.IgnoreCase));
        await Expect(Page.Locator(".alert--success, .alert:has-text('created successfully')").First).ToBeVisibleAsync();
        await Expect(Page.Locator($"text={lessonTitle}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Open_Import_Dialog_And_Import_Lesson_Manifest()
    {
        await SignInAsync("admin@demo.local", "Demo1234!");

        await Page.GotoAsync($"{BaseUrl}/admin/lessons");

        // Open modal
        await Page.Locator("button:has-text('Import Manifest / Package')").ClickAsync();
        var dialog = Page.Locator("#import-dialog");
        await Expect(dialog).ToBeVisibleAsync();

        // Prepare manifest file
        var tempManifestPath = Path.Combine(Path.GetTempPath(), $"import-test-{Guid.NewGuid():N}.json");
        var manifestJson = """
        {
          "lessons": [
            {
              "title": "E2E Imported Grammar Lesson",
              "skill": "reading",
              "categoryCode": "GRAMMAR",
              "summary": "Imported via Playwright E2E test",
              "estimatedMinutes": 5,
              "displayOrder": 99,
              "cards": [
                {
                  "type": "rule",
                  "title": "Imported Rule",
                  "contentMarkdown": "Rule content imported from JSON manifest."
                }
              ]
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(tempManifestPath, manifestJson);

        try
        {
            // Select certificate
            var certSelect = Page.Locator("#import-certId");
            await certSelect.SelectOptionAsync(new[] { "1" });

            // Attach file
            await Page.Locator("#import-file").SetInputFilesAsync(tempManifestPath);

            // Check overwrite option
            await Page.Locator("input[name='overwrite']").CheckAsync();

            // Submit upload
            await Page.Locator("button:has-text('Upload & Ingest')").ClickAsync();

            // Expect completion
            await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/lessons", RegexOptions.IgnoreCase));
            await Expect(Page.Locator(".alert--success, .alert:has-text('Import completed')").First).ToBeVisibleAsync();
            await Expect(Page.Locator("text=E2E Imported Grammar Lesson")).ToBeVisibleAsync();
        }
        finally
        {
            if (File.Exists(tempManifestPath))
            {
                File.Delete(tempManifestPath);
            }
        }
    }
}
