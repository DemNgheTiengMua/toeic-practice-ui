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

    [Fact]
    public void Take_View_Contains_SplitScreen_TopControlBar_Palette_Highlighter_And_ComponentWidgets_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Exams", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. Split-Screen Viewport dual-pane layout & independent scrollbars
        Assert.Contains(".exam-layout--split", html);
        Assert.Contains("id=\"split-stimulus-pane\"", html);
        Assert.Contains("id=\"split-passage-scroll\"", html);
        Assert.Contains("id=\"split-passage-text\"", html);

        // 2. Top Control Bar: Red countdown flash placard, volume slider, accessibility modal
        Assert.Contains("keyframes timer-pulse-red", html);
        Assert.Contains(".timer--warn-flash", html);
        Assert.Contains(".timer-alert-placard", html);
        Assert.Contains("id=\"exam-volume-slider\"", html);
        Assert.Contains("id=\"exam-volume-pct\"", html);
        Assert.Contains("id=\"accessibility-modal\"", html);
        Assert.Contains("data-size=\"standard\"", html);
        Assert.Contains("data-size=\"large\"", html);
        Assert.Contains("data-size=\"xlarge\"", html);
        Assert.Contains("data-theme=\"standard\"", html);
        Assert.Contains("data-theme=\"inverted\"", html);
        Assert.Contains("data-theme=\"high-contrast\"", html);

        // 3. Question Component Widgets Container and Renderers
        Assert.Contains("id=\"component-widget-container\"", html);
        Assert.Contains("renderComponentWidget", html);
        Assert.Contains("multi-select-widget", html);
        Assert.Contains("inline-completion-widget", html);
        Assert.Contains("dropdown-widget", html);
        Assert.Contains("drag-drop-widget", html);

        // 4. Bottom Question Palette with 4 status states
        Assert.Contains("id=\"bottom-question-palette\"", html);
        Assert.Contains("id=\"bottom-palette-chips\"", html);
        Assert.Contains("palette-chip--unanswered", html);
        Assert.Contains("palette-chip--answered", html);
        Assert.Contains("palette-chip--current", html);
        Assert.Contains("palette-chip--flagged", html);

        // 5. Tokenized Text Highlighter and Notes Engine
        Assert.Contains("id=\"highlighter-popover\"", html);
        Assert.Contains("data-action=\"highlight\"", html);
        Assert.Contains("data-action=\"note\"", html);
        Assert.Contains("data-action=\"clear\"", html);
        Assert.Contains("id=\"sticky-note-modal\"", html);
        Assert.Contains("renderTokenizedText", html);
        Assert.Contains("ee-token", html);
        Assert.Contains("candidateNotes", html);
    }
}