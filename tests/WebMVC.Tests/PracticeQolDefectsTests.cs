using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Regression tests for the Practice Mode quality-of-life defects recorded in
/// docs/specs/sitting-room-navigation-and-practice-qol-defects.md (tickets d06, d07, d08).
/// </summary>
public sealed class PracticeQolDefectsTests
{
    [Fact]
    public void Hint_Control_Restores_Itself_When_The_Candidate_Changes_Question()
    {
        var view = ReadPracticeTake();

        // revealHint disables the control while the request is in flight; renderQuestion must
        // re-enable it, otherwise the button stays dead for the rest of the session (ticket d06).
        Assert.Contains("showHintBtn.disabled = false", view, StringComparison.Ordinal);
        Assert.Matches(@"showHintBtn\.disabled\s*=\s*false", view);
    }

    [Fact]
    public void Revealed_Hint_With_No_Text_Hides_The_Hint_Box_And_The_Used_Badge()
    {
        var view = ReadPracticeTake();

        // A question with no authored hint returns hint: null; the UI must not claim 'hint used'
        // while showing an empty box (ticket d06).
        Assert.Matches(@"if\s*\(\s*s\.hintUsed\s*&&\s*s\.hint\s*\)", view);
        Assert.DoesNotMatch(@"hintUsedMsg\.style\.display\s*=\s*s\.hintUsed\s*\?", view);
    }

    [Fact]
    public void Transcript_Toggle_Sits_Inside_The_Audio_Toolbar()
    {
        var view = ReadPracticeTake();

        // ticket d07: the rewind and transcript controls belong to the audio player cluster,
        // not floating in the page body beneath it.
        var toolbarIndex = view.IndexOf("id=\"practice-audio-toolbar\"", StringComparison.Ordinal);
        Assert.True(toolbarIndex >= 0, "The audio toolbar container must exist");

        var rewindIndex = view.IndexOf("id=\"btn-audio-rewind-5s\"", StringComparison.Ordinal);
        Assert.True(rewindIndex > toolbarIndex, "The rewind control must render inside the audio toolbar");

        var toolbarClose = view.IndexOf("</section>", toolbarIndex, StringComparison.Ordinal);
        if (toolbarClose > 0)
        {
            Assert.True(rewindIndex < toolbarClose, "The rewind control must not render after the audio toolbar closes");
        }
    }

    [Fact]
    public void Sidebar_Filter_Row_Cannot_Overflow_Its_Card()
    {
        var view = ReadPracticeTake();

        // ticket d08: the three filter pills overflow the sidebar card at desktop width.
        // The row must be allowed to wrap; assert on the element's own opening tag.
        var tag = Regex.Match(view, @"<div[^>]*id=""navigator-filters""[^>]*>");
        Assert.True(tag.Success, "The navigator filter row must exist");
        Assert.Matches(@"flex-wrap\s*:\s*wrap", tag.Value);
    }

    [Fact]
    public void Component_Widget_Is_Cleared_Before_The_Next_Question_Renders()
    {
        var view = ReadPracticeTake();

        // ticket d08: the previous question's controls must be removed, not left hidden,
        // so a stale radio can never be mistaken for a current answer.
        Assert.Matches(@"componentContainer\.innerHTML\s*=\s*""""", view);
    }

    private static string ReadPracticeTake()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var path = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml");
        Assert.True(File.Exists(path), "Practice/Take.cshtml must exist");
        return File.ReadAllText(path);
    }
}
