using System.IO;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #189: the quick-ingest dialog carries the pool coordinates of a paste.
/// <para>
/// A paste is text, so a CEFR level cannot be read off it and D1 forbids deriving one from the
/// difficulty number. The dialog therefore asks once per batch, and the list of topics follows the
/// selected section's skill - a listening paste must not be filed under a reading topic. Without
/// these controls the commit writes the legacy rows only, which is legal but leaves the pool empty.
/// </para>
/// </summary>
public sealed class QuickIngestPoolControlsTests
{
    private static string Root => TestPathHelper.FindRepoRoot();

    [Fact]
    public void The_dialog_asks_for_a_level_and_a_topic_and_both_are_wired_into_the_commit()
    {
        var modal = File.ReadAllText(Path.Combine(Root, "src", "WebMVC", "Views", "Admin", "_QuickIngestModal.cshtml"));
        var script = File.ReadAllText(Path.Combine(Root, "src", "WebMVC", "wwwroot", "js", "admin", "quick-ingest.js"));

        // The controls exist and are reachable.
        Assert.Contains("id=\"qi-cefr-level\"", modal);
        Assert.Contains("id=\"qi-topic-code\"", modal);
        // A1-C2 is the authored ladder; pre-A1 is reportable only, so it is not offered.
        Assert.Contains("<option value=\"A1\">", modal);
        Assert.Contains("<option value=\"C2\">", modal);
        Assert.DoesNotContain("pre_a1", modal);

        // …and the commit carries them, so the dialog is not decoration.
        Assert.Contains("cefrLevel: cefrLevelSelect", script);
        Assert.Contains("topicCode: topicCodeSelect", script);

        // The topic list is the canonical twenty, filtered by the section's own skill.
        Assert.Contains("'R-GIST'", script);
        Assert.Contains("'L-GIST'", script);
        Assert.Contains("refreshTopicOptions(modality)", script);
    }
}
