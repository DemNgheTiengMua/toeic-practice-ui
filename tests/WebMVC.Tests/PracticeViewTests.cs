using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class PracticeViewTests
{
    [Fact]
    public void Take_View_Contains_PracticeExamShell_And_ZeroReload_Invariants()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Take.cshtml must exist");
        var html = File.ReadAllText(viewPath);

        // 1. Two-column Exam Shell layout
        Assert.Contains("exam-layout", html);

        // 2. Embedded JSON manifest for 0ms in-memory swapping
        Assert.Contains("id=\"practice-data\"", html);

        // 3. Persistent Sidebar Navigator Grid with button cells
        Assert.Contains("class=\"navigator__grid\"", html);
        Assert.Contains("<button type=\"button\"", html);

        // 4. Zero-Reload: Navigator and next actions must NOT have href question links
        Assert.DoesNotMatch(@"<a\s+[^>]*href=[""']/practice/\d+\?q=", html);

        // 5. Practice Quality of Life (QoL) controls
        Assert.Contains("id=\"btn-controls-modal\"", html);
        Assert.Contains("id=\"controls-modal\"", html);
        Assert.Contains("data-filter=\"all\"", html);
        Assert.Contains("data-filter=\"incorrect\"", html);
        Assert.Contains("data-filter=\"saved\"", html);
        Assert.Contains("id=\"sidebar-accuracy-display\"", html);
    }

    [Fact]
    public void StudyAudioPlayer_Contains_Speed_Toggles_And_No_Robotic_Disclaimers()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var markupPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_ExamAudioPlayerMarkup.cshtml");

        Assert.True(File.Exists(markupPath), "_ExamAudioPlayerMarkup.cshtml must exist");
        var html = File.ReadAllText(markupPath);

        // Speed toggles for study mode ear-training
        Assert.Contains("data-speed=\"0.8\"", html);
        Assert.Contains("data-speed=\"1.0\"", html);
        Assert.Contains("data-speed=\"1.2\"", html);

        // Strict non-regression guard: No robotic disclaimer copy
        Assert.DoesNotContain("Seeking disabled", html);
        Assert.DoesNotContain("Plays once only", html);
        Assert.DoesNotContain("Replay permitted", html);
    }
}
