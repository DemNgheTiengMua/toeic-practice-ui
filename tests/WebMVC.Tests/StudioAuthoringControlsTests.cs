using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #159: authoring 200 questions must stop being painful. The three panes, the ~18 visible inputs
/// of a 4-option MCQ, the free-text AudioPath/ImagePath boxes and the typed milliseconds were the
/// pain named by the ticket. These tests pin the shape of the rebuilt studio so it cannot drift
/// back: two panes, one Category dropdown, a file picker for media, and slice bounds taken from a
/// player.
/// </summary>
public sealed class StudioAuthoringControlsTests
{
    private static string Views => Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Admin");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Views, file));

    /// <summary>Controls the author can reach without opening anything: hidden inputs and controls
    /// inside a closed &lt;details&gt; are excluded.</summary>
    private static int CountVisibleControls(string region)
    {
        var withoutDetails = Regex.Replace(region, "<details[^>]*>.*?</details>", "", RegexOptions.Singleline);
        var withoutHidden = Regex.Replace(withoutDetails, "<input[^>]*type=\"hidden\"[^>]*/?>", "");
        return Regex.Matches(withoutHidden, "<(input|select|textarea|button)\\b").Count;
    }

    [Fact]
    public void The_studio_is_two_panes_and_the_publish_audit_sits_inside_the_workstation()
    {
        var shell = Read("ExamStudio.cshtml");
        var canvas = Read("_StudioCanvas.cshtml");

        // The audit used to be the third column. It is still on the page — the publish gate (#157)
        // must stay visible — but it is docked in the workstation now.
        Assert.DoesNotContain("_StudioAudit", shell);
        Assert.Contains("_StudioAudit", canvas);

        var css = File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(),
            "src", "WebMVC", "wwwroot", "css", "admin", "exam-studio.css"));
        var baseGrid = Regex.Match(css, @"\.studio-container\s*\{[^}]*grid-template-columns:\s*([^;]+);");
        Assert.True(baseGrid.Success, "the studio grid must be declared in exam-studio.css");
        Assert.Equal(2, baseGrid.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>
    /// The count the ticket names. The Razor loop renders one row template four times, so the
    /// rendered total is (controls outside the loop) + 4 x (controls in one row). Everything the
    /// author does not see — the other question-type panels, the per-option rationales — is not
    /// counted.
    /// </summary>
    [Fact]
    public void A_four_option_mcq_renders_fourteen_visible_controls()
    {
        var question = Read("_StudioQuestion.cshtml");

        var mcqStart = question.IndexOf("<!-- 1. COMPONENT PANEL: MCQ", StringComparison.Ordinal);
        var rowStart = question.IndexOf("class=\"row studio-opt-row-single\"", mcqStart, StringComparison.Ordinal);
        // Cut at the <details> opener, so the collapsed rationale block is excluded as a whole.
        var rowEnd = question.IndexOf("<details", rowStart, StringComparison.Ordinal);
        var multiStart = question.IndexOf("<!-- 2. COMPONENT PANEL: MULTI-SELECT", StringComparison.Ordinal);
        var explanationStart = question.IndexOf("<!-- Pedagogical Explanation", StringComparison.Ordinal);
        Assert.True(mcqStart > 0 && rowStart > mcqStart && rowEnd > rowStart && multiStart > rowEnd && explanationStart > multiStart);

        var row = question[rowStart..rowEnd];
        var oneRow = CountVisibleControls(row);
        // One correct-answer radio and one option text. Nothing else per option.
        Assert.Equal(2, oneRow);

        var outsideLoop = CountVisibleControls(question[..rowStart])
            + CountVisibleControls(question[rowEnd..multiStart])
            + CountVisibleControls(question[explanationStart..]);
        var rendered = outsideLoop + 4 * oneRow;

        // Was 18: type select, category select, remove, stem, Insert Blank, 4 radios, 4 option
        // texts, 4 per-option rationale boxes, explanation.
        // Was 14 after #159. #229 adds the level picker: an item is authored at a level, so the card
        // that authors it has to offer one.
        Assert.Equal(15, rendered);

        // Rationales are still editable, but they no longer occupy a line per option: one template
        // row inside a collapsed <details>, rendered by a loop.
        var rationales = question[rowEnd..multiStart];
        Assert.Contains("<details", rationales);
        Assert.Contains("<summary", rationales);
        Assert.Single(Regex.Matches(rationales, "class=\"field__input studio-opt-rationale\""));
        Assert.Contains("foreach (var option in Model.RadioOptions)", rationales);
    }

    [Fact]
    public void A_question_has_exactly_one_category_dropdown()
    {
        var question = Read("_StudioQuestion.cshtml");

        // One dropdown for the whole card. A per-option category was part of what made 18 inputs.
        Assert.Single(Regex.Matches(question, "data-field=\"CategoryId\""));
        Assert.Single(Regex.Matches(question, ">Category:</label>"));
    }

    [Fact]
    public void Media_is_chosen_with_a_file_picker_not_typed_as_a_path()
    {
        var canvas = Read("_StudioCanvas.cshtml");

        // The free-text boxes that failed silently are gone; the value they wrote still travels in
        // a hidden field, so the autosave payload did not have to change.
        Assert.DoesNotContain("Photo File Path", canvas);
        Assert.DoesNotContain("Audio Clip Track", canvas);
        Assert.Contains("type=\"hidden\" data-type=\"group\" data-id=\"@g.GroupId\" data-field=\"ImagePath\"", canvas);        Assert.Contains("type=\"hidden\" data-type=\"group\" data-id=\"@g.GroupId\" data-field=\"AudioPath\"", canvas);
        Assert.Contains("accept=\"image/*\"", canvas);
        Assert.Contains("accept=\"audio/*\"", canvas);
        Assert.Contains("uploadGroupAudioFile(this, @g.GroupId)", canvas);
        Assert.Contains("uploadImageInput(this, @g.GroupId)", canvas);
    }

    [Fact]
    public void Slice_bounds_are_marked_from_the_player_not_typed_in_milliseconds()
    {
        var canvas = Read("_StudioCanvas.cshtml");

        Assert.DoesNotContain("Start (ms):", canvas);
        Assert.DoesNotContain("End (ms):", canvas);
        Assert.DoesNotContain("Sync Slicer", canvas);

        // Two buttons per group, and the two hidden fields that carry what they write. One audio
        // element per section, not one per group: a 40-group section still loads one track.
        Assert.Contains("data-slice=\"start\"", canvas);
        Assert.Contains("data-slice=\"end\"", canvas);
        Assert.Contains("onclick=\"markGroupSliceStart(@g.GroupId)\"", canvas);
        Assert.Contains("onclick=\"markGroupSliceEnd(@g.GroupId)\"", canvas);
        Assert.Contains("id=\"start-ms-@g.GroupId\"", canvas);
        Assert.Contains("id=\"end-ms-@g.GroupId\"", canvas);
        Assert.Single(Regex.Matches(canvas, "id=\"slicer-audio-@sec.SectionId\""));
    }

    /// <summary>The script is where the two buttons become milliseconds, so pin that seam: the
    /// value is the player's position floored to whole milliseconds, and the pair is rejected
    /// unless end &gt; start (CK_QGroups_AudioSlice rejects the row otherwise).</summary>
    [Fact]
    public void The_script_floors_the_player_position_and_refuses_an_end_before_the_start()
    {
        var script = File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(),
            "src", "WebMVC", "wwwroot", "js", "admin", "exam-studio.js"));

        Assert.Contains("Math.floor(audio.currentTime * 1000)", script);
        Assert.Contains("endMs <= startMs", script);
        Assert.Contains("setSliceStatus(groupId, 'End must come after start", script);
        Assert.Contains("input.value = String(ms)", script);
        Assert.Contains("endInput.value = String(endMs)", script);
    }
}
