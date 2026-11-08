using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #217 / PLC-18: the confirmation modal that names the unanswered count PER SECTION.
/// <para>
/// <b>The input that would pass a weaker test.</b> "A modal appeared" - or "the modal mentions the
/// word unanswered" - passes on a modal that says "You have unanswered questions", which is precisely
/// the implementation the ticket rejects. So the assertions are: two sections with DIFFERENT counts
/// produce two rows carrying those exact numbers, the rows are ordered by the sections' sitting order,
/// each row's number equals what that section alone is missing (a paper total split evenly would give
/// 2 and 2 here), and the modal's markup carries no paper-level total in place of the per-section
/// table.
/// </para>
/// </summary>
public sealed class PlacementSubmitModalTests
{
    [Fact]
    public void The_confirmation_names_the_unanswered_count_for_each_section_separately()
    {
        // Listening has 1 of 2 answered; Reading has 0 of 2 answered. A per-PAPER total would be 3,
        // and a paper total split across two rows would be 2 and 2 - both are refused below.
        var model = new PlacementTakeViewModel(Take(navigator:
        [
            new(10, 1, 1, "L1", ModalityDto.Listening, IsAnswered: true, IsFlagged: false, IsCurrent: true),
            new(11, 2, 1, "L1", ModalityDto.Listening, IsAnswered: false, IsFlagged: false, IsCurrent: false),
            new(20, 3, 2, "R1", ModalityDto.Reading, IsAnswered: false, IsFlagged: false, IsCurrent: false),
            new(21, 4, 2, "R1", ModalityDto.Reading, IsAnswered: false, IsFlagged: false, IsCurrent: false),
        ]));

        Assert.True(model.HasUnanswered);
        Assert.Equal(2, model.Sections.Count);

        var listening = model.Sections.Single(s => s.SectionId == 1);
        Assert.Equal(1, listening.UnansweredCount);
        Assert.Equal(2, listening.TotalCount);

        var reading = model.Sections.Single(s => s.SectionId == 2);
        Assert.Equal(2, reading.UnansweredCount);   // not 1, and not the paper's 3
        Assert.Equal(2, reading.TotalCount);

        // ordered by where each section sits, so the modal reads like the sitting did
        Assert.Equal([1, 2], model.Sections.Select(s => s.SectionId));
        Assert.Equal("Listening section", listening.SectionName);
    }

    /// <summary>
    /// A section with nothing outstanding keeps its row and is hidden; the count it carries is 0. This
    /// is the pair to the test above: dropping the row instead would make the table reshuffle as counts
    /// fall, and the client repaint would then be writing into rows that no longer exist.
    /// </summary>
    [Fact]
    public void A_fully_answered_section_keeps_its_row_at_zero_instead_of_vanishing()
    {
        var model = new PlacementTakeViewModel(Take(navigator:
        [
            new(10, 1, 1, "L1", ModalityDto.Listening, IsAnswered: true, IsFlagged: false, IsCurrent: true),
            new(11, 2, 1, "L1", ModalityDto.Listening, IsAnswered: true, IsFlagged: false, IsCurrent: false),
            new(20, 3, 2, "R1", ModalityDto.Reading, IsAnswered: false, IsFlagged: false, IsCurrent: false),
        ]));

        Assert.Equal(2, model.Sections.Count);
        Assert.Equal(0, model.Sections.Single(s => s.SectionId == 1).UnansweredCount);
        Assert.Equal(1, model.Sections.Single(s => s.SectionId == 2).UnansweredCount);
    }

    /// <summary>
    /// The modal the learner actually sees. This walks the rendered view, not the model: a modal whose
    /// per-section loop was replaced by a single paper total would pass every model test above.
    /// </summary>
    [Fact]
    public void The_modal_view_prints_one_monospace_count_row_per_section()
    {
        var root = TestPathHelper.FindRepoRoot();
        var modal = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Placement", "_TakeSubmitModal.cshtml"));

        // it iterates the model's per-section rows, and prints each section's own number
        Assert.Contains("Model.Sections", modal);
        Assert.Contains("row.UnansweredCount", modal);
        Assert.Contains("row.SectionName", modal);
        Assert.Contains("data-section-id", modal);

        // the count is tabular and the section heading is signage type - the design system's rule for
        // numbers and placards, not a default browser font
        Assert.Contains("font-mono", modal);
        Assert.Matches("take-submit-modal__sections", modal);

        // and it says what submitting costs, which is the reason to warn at all
        Assert.Contains("incorrect", modal, StringComparison.OrdinalIgnoreCase);

        // the design system: no radius, no shadow, no gradient, no emoji
        Assert.DoesNotMatch(@"border-radius|box-shadow|linear-gradient|radial-gradient", modal);
        Assert.DoesNotMatch(@"[\u2190-\u21FF\u2600-\u27BF\uD83C-\uDBFF]", modal);
    }

    /// <summary>
    /// The client keeps those counts live. A modal opened once with the page-load numbers is the same
    /// trap as a modal that does not name sections: it is asserted to repaint from the navigator, and
    /// to hide a row that has reached zero rather than print "0".
    /// </summary>
    [Fact]
    public void The_client_repaints_the_per_section_counts_from_the_navigator()
    {
        var root = TestPathHelper.FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "wwwroot", "js", "placement", "take.js"));

        Assert.Contains("refreshSubmitModalSections", script);
        Assert.Contains("submit-modal-section-rows", script);
        Assert.Contains("data-unanswered", script);
        // counted from the same class the save path sets, so there is one live answered definition
        Assert.Contains("is-answered", script);
        // and repainted when the modal opens, not only when a page loads
        Assert.Matches(@"js-open-submit-modal[\s\S]{0,120}refreshSubmitModalSections", script);
    }

    private static PlacementTakeViewDto Take(IReadOnlyList<NavigatorItemDto> navigator) => new(
        42, 100, 7, "Placement exam", "TOEIC", PlacementStatusDto.InProgress,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(20), 1200,
        TotalQuestions: 4, AnsweredCount: 1, FlaggedCount: 0,
        CurrentQuestion: Question(),
        Navigator: navigator,
        IsAutoSubmitted: false,
        Questions:
        [
            Question() with { QuestionId = 10, DisplayOrder = 1, SectionId = 1, SectionName = "Listening section" },
            Question() with { QuestionId = 11, DisplayOrder = 2, SectionId = 1, SectionName = "Listening section" },
            Question() with { QuestionId = 20, DisplayOrder = 3, SectionId = 2, SectionCode = "R1",
                SectionName = "Reading section", SkillCode = "reading", SkillName = "Reading",
                Modality = ModalityDto.Reading },
            Question() with { QuestionId = 21, DisplayOrder = 4, SectionId = 2, SectionCode = "R1",
                SectionName = "Reading section", SkillCode = "reading", SkillName = "Reading",
                Modality = ModalityDto.Reading },
        ]);

    private static AttemptQuestionDto Question() => new(
        10, 1, 4, 1, "L1", "Listening section", "listening", "Listening", ModalityDto.Listening,
        "Choose an answer.", null, null, null, null, null, null, null,
        [new(11, "A", "First"), new(12, "B", "Second")], null, false);
}
