using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// The audit trail the objective asks for: one screenshot per screen, in a per-flow folder, taken by the
/// browser the journeys already use.
/// <para>
/// <b>Why a journey and not a manual pass.</b> A screenshot taken by hand proves a screen looked right once,
/// on one machine, at one moment. Taken here it is reproducible: the same crawl environment, the same seed,
/// the same steps, and a re-run overwrites the file rather than adding a second one nobody knows the age of.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It asserts nothing about pixel values. Its subject is that every
/// screen in the flow CAN be reached and drawn with the seeded data - the assertions about content belong to
/// the journeys beside it, and a screenshot suite that also asserted would be two tests on one fact.
/// </para>
/// </summary>
public class AuditScreenshotTests : PlaywrightTestBase
{
    private static string ShotDir
    {
        get
        {
            // tools/../docs is the repo's own tree, so the images sit beside the audit notes that cite them.
            var dir = Path.Combine(RepoRoot(), "docs", "audit", "screenshots", "rebuild");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExtraEfficiency.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
    }

    private async Task CaptureAsync(string flow, string name)
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{flow}--{name}.png"),
            FullPage = true,
        });
    }

    [Test]
    public async Task Learner_flow()
    {
        const string flow = "learner";

        await Page.GotoAsync($"{BaseUrl}/auth/sign-in");
        await CaptureAsync(flow, "01-sign-in");

        await SignInAsync("learner@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/certificates");
        await CaptureAsync(flow, "02-certificates");

        await Page.GotoAsync($"{BaseUrl}/Dashboard");
        await CaptureAsync(flow, "03-dashboard");

        await Page.GotoAsync($"{BaseUrl}/practice");
        await CaptureAsync(flow, "04-practice-hub");

        await Page.GotoAsync($"{BaseUrl}/placement");
        await CaptureAsync(flow, "05-placement");
    }

    [Test]
    public async Task Admin_flow()
    {
        const string flow = "admin";

        await SignInAsync("admin@demo.local", "Demo1234!");
        await Page.GotoAsync($"{BaseUrl}/Admin");
        await CaptureAsync(flow, "01-admin-dashboard");

        await Page.GotoAsync($"{BaseUrl}/Admin/Exams");
        await CaptureAsync(flow, "02-exam-catalogue");

        // Exam 1 is the paper the sitting journeys use. Studio, not /edit: /edit is the metadata form and
        // /studio is the assembly surface this audit is about.
        await Page.GotoAsync($"{BaseUrl}/Admin/Exams/1/studio");
        await CaptureAsync(flow, "03-exam-studio");

        // There is no GET /admin/items page - the pool item routes are the POST/PUT write door only
        // (AdminItemsController has no HttpGet), so the pool is read through an exam's studio above.
        // Import is a modal on the exam list, captured as part of 02.
        await Page.GotoAsync($"{BaseUrl}/Admin/Exams/create");
        await CaptureAsync(flow, "04-exam-create");
    }
}
