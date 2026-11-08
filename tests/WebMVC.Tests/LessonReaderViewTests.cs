using System.IO;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #240: the leveled lesson reader's page-level claims, which the service tests cannot make.
/// <para>
/// The two guards here are the ones a working service can still ship a broken page through:
/// the key must not be in the markup (a correct grade is worth nothing if the browser can read the
/// answer first), and the page must name which module it is showing (otherwise the remedial visit is
/// the primary one with a different heading and nobody can tell).
/// </para>
/// </summary>
public sealed class LessonReaderViewTests
{
    private static string View(string name) =>
        File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Lessons", name));

    [Fact]
    public void The_reader_does_not_ship_the_answer_or_its_explanation()
    {
        var cards = View("_PlayerCards.cshtml");

        // The key is revealed by the graded reply, never by the markup.
        Assert.DoesNotContain("data-correct", cards);
        Assert.DoesNotContain("card.CorrectOption", cards);

        // The wrong input this catches: rendering Explanation behind display:none. The seeded copy
        // names the right option ("(B) is evaluating"), so hidden-but-present is still the answer in
        // the page source. Only the graded reply may supply it.
        Assert.DoesNotContain("card.Explanation", cards);
        Assert.Contains("microCheckReasonText", cards);
    }

    [Fact]
    public void The_reader_names_its_cell_and_its_module_and_can_switch_modules()
    {
        var index = View("Index.cshtml");

        // Criterion 1 is a visible claim: the lesson says its own topic and level.
        Assert.Contains("TopicCode", index);
        Assert.Contains("CefrLevel", index);
        Assert.Contains("ModuleTitle", index);

        // Criterion 2: the two modules are reachable from one another, so a second attempt can read
        // the remedial one instead of the same cards again.
        Assert.Contains("HasRemedialModule", index);
        Assert.Contains("ModuleUrl(Model.OtherModule)", index);
    }

    [Fact]
    public void The_reader_opens_on_the_resume_index_and_not_on_card_one()
    {
        var index = View("Index.cshtml");

        // The page takes the index from the model's computed resume point; hardcoding 1 is the
        // "restart at card one" defect the service test catches at the service, and this catches at
        // the page.
        Assert.Contains("initialIndex = Model.InitialIndex", index);
        Assert.Contains("data-initial-index=\"@initialIndex\"", index);
        Assert.DoesNotContain("var initialIndex = 1;", index);
    }

    [Fact]
    public void An_empty_module_says_so_instead_of_rendering_a_blank_deck()
    {
        var index = View("Index.cshtml");

        // A module nobody has authored yet is a content gap, said plainly. A reader that renders zero
        // cards looks broken and gives the learner nothing to do.
        Assert.Contains("totalCards == 0", index);
        Assert.Contains("This module has no cards yet", index);
    }
}
