using Contracts;
using System.Text.Json;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #238: the objective practice session's three payload facts — the level reveal, the review bound to
/// its evidence, and a short drill presented as short.
/// <para>
/// Every assertion here is on the <b>serialized manifest the take page receives</b> or on the view
/// model the page renders from. None of them reads markup, because a level hidden by a CSS class while
/// the payload still carries it is exactly the defect the ticket names.
/// </para>
/// </summary>
public sealed class PracticeObjectiveSessionTests
{
    private static PracticeSessionDto CellSession(string? level = "B2") =>
        new(42, null, "R-DET", "Detail", ModalityDto.Reading, 3, PracticeStatusDto.InProgress, 0,
            QuestionSourceDto.All, DateTimeOffset.UtcNow, null, TopicId: 5, CefrLevelCode: level);

    private static PracticeQuestionDto Question(int order, int? selected = null, bool? correct = null,
        string? level = null) =>
        new(42, "Detail", null, order, 3, $"Stem {order}", null, null, false,
            [new(11, "A", "First"), new(12, "B", "Second")],
            selected, correct, correct.HasValue ? 12 : null, null, null, null, null, null, null,
            CefrLevelCode: level);

    private static JsonElement Manifest(PracticeTakePresentation model) =>
        JsonDocument.Parse(model.QuestionsJson).RootElement.Clone();

    /// <summary>
    /// The guard. The wrong input that would pass a weaker test is an unanswered question whose DTO
    /// already carries a level: hiding it in markup would still leak it to anyone reading the payload.
    /// So the fixture sets the level on the DTO, and the manifest must still omit it.
    /// </summary>
    [Fact]
    public void An_unanswered_question_carries_no_level_in_the_manifest_even_when_the_dto_does()
    {
        var unanswered = Question(1, level: "B2");
        var model = new PracticeTakePresentation(CellSession(), unanswered, [unanswered], null);

        var first = Manifest(model)[0];
        Assert.False(first.TryGetProperty("cefrLevelCode", out var level) && level.ValueKind != JsonValueKind.Null,
            "an unanswered question must not name its level in the payload");
    }

    /// <summary>
    /// The other half: once the answer is locked the level is revealed, both on the current question
    /// and for every locked question in the manifest.
    /// </summary>
    [Fact]
    public void A_locked_question_reveals_its_level_in_the_manifest_and_on_the_current_question()
    {
        var locked = Question(1, selected: 11, correct: true);
        var model = new PracticeTakePresentation(CellSession(), locked, [locked], null);

        Assert.Equal("B2", model.RevealedLevelCode);
        Assert.Equal("B2", Manifest(model)[0].GetProperty("cefrLevelCode").GetString());
    }

    /// <summary>
    /// A section drill names no cell, so it has no level to reveal. Inventing one from the section would
    /// be the fabrication the ticket forbids; the reveal stays silent instead.
    /// </summary>
    [Fact]
    public void A_section_drill_with_no_cell_reveals_no_level()
    {
        var locked = Question(1, selected: 11, correct: true);
        var sectionSession = new PracticeSessionDto(42, 5, "P5", "Part 5", ModalityDto.Reading, 3,
            PracticeStatusDto.InProgress, 0, QuestionSourceDto.All, DateTimeOffset.UtcNow, null);

        var model = new PracticeTakePresentation(sectionSession, locked, [locked], null);

        Assert.Null(model.RevealedLevelCode);
    }

    /// <summary>
    /// "Bound to its evidence" means the explanation names the option it explains. The wrong input that
    /// would pass a weaker test is a free-floating paragraph with no subject — so the assertion is the
    /// option label and its text, not merely that an explanation exists.
    /// </summary>
    [Fact]
    public void The_review_binds_the_explanation_to_the_option_it_explains()
    {
        var locked = Question(1, selected: 11, correct: false) with
        {
            Explanation = "The passage says Tuesday.",
        };
        var model = new PracticeTakePresentation(CellSession(), locked, [locked], null);

        Assert.Equal("B", model.ExplanationSubjectLabel);
        Assert.Equal("Second", model.ExplanationSubjectText);
    }

    [Fact]
    public void A_free_response_review_has_no_option_to_bind_to()
    {
        var locked = Question(1, selected: null, correct: true) with
        {
            QuestionType = "free_response",
            Explanation = "Under length.",
        };
        var model = new PracticeTakePresentation(CellSession(), locked, [locked], null);

        Assert.Null(model.ExplanationSubjectLabel);
    }

    /// <summary>
    /// #235 already marks a pool drill short on the session DTO. #238 requires it be *presented* as
    /// short rather than padded — the wrong input being a page that shows a short drill and says
    /// nothing about it.
    /// </summary>
    [Fact]
    public void A_short_drill_is_presented_as_short()
    {
        var session = new PracticeSessionDto(42, null, "R-DET", "Detail", ModalityDto.Reading, 4,
            PracticeStatusDto.InProgress, 0, QuestionSourceDto.All, DateTimeOffset.UtcNow, null,
            TopicId: 5, CefrLevelCode: "B2");
        var question = Question(1);

        var model = new PracticeTakePresentation(session, question, [question], null);

        Assert.True(model.IsShort);
        Assert.Contains("short", model.ShortNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_full_drill_says_nothing_about_being_short()
    {
        var question = Question(1);
        var fullSession = CellSession() with { QuestionCount = 10 };
        var model = new PracticeTakePresentation(fullSession, question, [question], null);

        Assert.False(model.IsShort);
        Assert.Null(model.ShortNotice);
    }
}
