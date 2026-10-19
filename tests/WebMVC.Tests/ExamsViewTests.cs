using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class ExamsViewTests
{
    [Fact]
    public void OptionMarkerAndText_MustHavePointerEventsNone_ToAvoidInterceptingClicks()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var cssPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components.css");

        Assert.True(File.Exists(cssPath), "components.css must exist");
        var cssContent = File.ReadAllText(cssPath);

        // Match .option__marker block
        var markerMatch = Regex.Match(cssContent, @"\.option__marker\s*\{(?<rules>[^}]+)\}");
        Assert.True(markerMatch.Success, ".option__marker rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", markerMatch.Groups["rules"].Value);

        // Match .option__text block
        var textMatch = Regex.Match(cssContent, @"\.option__text\s*\{(?<rules>[^}]+)\}");
        Assert.True(textMatch.Success, ".option__text rule block must be found in components.css");
        Assert.Matches(@"pointer-events\s*:\s*none", textMatch.Groups["rules"].Value);
    }

    [Fact]
    public void Take_View_Contains_ExamShell_Integrity_And_ZeroReload_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. Embedded JSON payload for 0ms in-memory swapping
        Assert.Contains("id=\"exam-data\"", html);

        // 2. Exam Integrity Disciplinary Warning Banner
        Assert.Contains("id=\"integrity-warning\"", html);
        Assert.Contains("id=\"integrity-count\"", html);

        // 3. Offline Network Banner
        Assert.Contains("id=\"network-warning\"", html);

        // 4. Modal In-flight Sync Warning
        Assert.Contains("id=\"modal-sync-pending\"", html);

        // 5. Zero-Reload: Navigator cells must be buttons, NOT anchor tags with href navigation
        Assert.Contains("<button type=\"button\"", html);
        Assert.DoesNotMatch(@"<a\s+[^>]*href=[""']/exams/take/[^""']*\?q=", html);

        // 6. Non-happy path guards
        Assert.Contains("window.history.pushState", html);
        Assert.Contains("popstate", html);
        Assert.Contains("beforeunload", html);
        Assert.Contains("contextmenu", html);
        Assert.Contains("visibilitychange", html);
    }
}
