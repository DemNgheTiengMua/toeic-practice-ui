using Contracts;
using System.Text.Json;
using WebMVC.Models;

namespace WebMVC.Tests;

public sealed class PracticePresentationTests
{
    // #149: a certificate whose only skill is Writing must see its practice content, not the
    // "No questions to practice yet" empty state that was keyed on Listening/Reading sections.
    // #150: and that content is drillable now, so the start form appears too.
    [Fact]
    public void A_writing_only_certificate_can_start_a_drill()
    {
        var model = new PracticeCatalogViewModel(new PracticeCatalogDto(
            new CertificateOptionDto("DEMO", "DEMO"),
            [], [],
            [new PracticeSectionDto(3, "WRIT", "Writing", ModalityDto.Writing, "T1", "Task 1", 4, 1, 0, null, 0, 1)],
            null));

        Assert.True(model.HasAnySections);
        Assert.Single(model.DrillableSectionGroups);
        Assert.Empty(model.SpeakingSections);
    }

    // #150 round 2: a spoken answer cannot be typed. A Speaking-only certificate still sees its
    // section, but as a link to the recorded, AI-graded practice - never as a drill button.
    [Fact]
    public void A_speaking_only_certificate_gets_no_typed_drill_and_links_to_recorded_practice()
    {
        var model = new PracticeCatalogViewModel(new PracticeCatalogDto(
            new CertificateOptionDto("DEMO", "DEMO"),
            [], [], [],
            null,
            SpeakingSections: [new PracticeSectionDto(9, "SPEA", "Speaking", ModalityDto.Speaking, "S1", "Interview", 4, 1, 0, null, 0, 1)],
            SpeakingPracticeExamId: 77));

        Assert.True(model.HasAnySections);
        Assert.Empty(model.DrillableSectionGroups);
        Assert.Single(model.SpeakingSections);
        Assert.Equal(77, model.SpeakingPracticeExamId);
    }

    [Fact]
    public void A_certificate_with_no_sections_at_all_shows_the_empty_state()
    {
        var model = new PracticeCatalogViewModel(new PracticeCatalogDto(
            new CertificateOptionDto("DEMO", "DEMO"), [], [], [], null));

        Assert.False(model.HasAnySections);
    }

    private static PracticeSessionDto Session(PracticeStatusDto status = PracticeStatusDto.InProgress) =>
        new(42, 5, "P5", "Reading", ModalityDto.Reading, 3, status, 1,
            QuestionSourceDto.All, DateTimeOffset.UtcNow, null);

    private static PracticeQuestionDto Question(int id, int? selected = null, bool? correct = null) =>
        new(42, "Reading", id, id, 3, "Question </script><script>alert(1)</script>", null, null, false,
            [new(11, "A", "First"), new(12, "B", "Second")], selected, correct,
            correct.HasValue ? 12 : null, null, null, null, null, null, null);

    [Fact]
    public void Finished_session_prepares_counts_navigator_and_wrong_answer_highlights()
    {
        var current = Question(2, 11, false) with { IsSaved = true };
        var model = new PracticeTakePresentation(Session(PracticeStatusDto.Finished), current,
            [Question(1, 12, true), current, Question(3)], null);

        Assert.Equal(2, model.AnsweredCount);
        Assert.Equal(1, model.CorrectCount);
        Assert.Equal(1, model.IncorrectCount);
        Assert.Equal(1, model.SkippedCount);
        Assert.Equal(1, model.UnansweredCount);
        Assert.Equal(1, model.SavedCount);
        Assert.Equal(50, model.AccuracyPercent);
        Assert.Equal(67, model.ProgressPercent);
        Assert.Equal("50% (1/2)", model.SidebarAccuracyDisplay);
        Assert.Equal("is-incorrect is-marked is-current", model.NavigatorCells[1].CssClass);
        Assert.Equal("is-skipped", model.NavigatorCells[2].CssClass);
        Assert.Equal("option option--wrong", model.Options[0].CssClass);
        Assert.Equal("option option--correct", model.Options[1].CssClass);
    }

    [Fact]
    public void A_selected_answer_that_is_not_graded_yet_is_counted_without_revealing_correctness()
    {
        var question = Question(1, 11);
        var model = new PracticeTakePresentation(Session(),
            question, [question, Question(2), Question(3)], null);

        Assert.Equal(1, model.AnsweredCount);
        Assert.Equal(0, model.CorrectCount);
        Assert.Equal(0, model.IncorrectCount);
        Assert.Equal(0, model.SkippedCount);
        Assert.Equal(33, model.ProgressPercent);
        Assert.True(model.IsCurrentAnswered);
        Assert.All(model.Options, option => Assert.Equal("option", option.CssClass));
        Assert.Equal("is-unanswered is-current", model.NavigatorCells[0].CssClass);
    }

    [Fact]
    public void Empty_session_has_zero_progress_and_no_division_by_zero()
    {
        var model = new PracticeTakePresentation(Session(), Question(1), [], null);
        Assert.Equal(0, model.TotalCount);
        Assert.Equal(0, model.ProgressPercent);
        Assert.Equal(0, model.AccuracyPercent);
        Assert.Equal("--%", model.TopbarAccuracyDisplay);
        Assert.Empty(model.NavigatorCells);
    }

    [Fact]
    public void Question_manifest_roundtrips_and_cannot_close_the_json_script_element()
    {
        var question = Question(1) with { AudioPath = "/audio/example.mp3", AudioStartMs = 1000, AudioEndMs = 2000 };
        var model = new PracticeTakePresentation(Session(), question, null, null);
        Assert.True(model.HasListening);
        Assert.Equal(1000, model.Audio.StartMs);
        Assert.Equal(2000, model.Audio.EndMs);
        Assert.True(model.AudioTemplate.TemplateOnly);
        Assert.DoesNotContain("</script>", model.QuestionsJson);
        using var document = JsonDocument.Parse(model.QuestionsJson);
        Assert.Equal(question.Stem, document.RootElement[0].GetProperty("stem").GetString());
    }
    [Fact]
    public void Insight_panel_prepares_safe_narrative_status_and_recommendation_badges()
    {
        var insight = new AiInsightDto(1, 7, 1, AiInsightScopeDto.PracticeSession, 42, null,
            "**Review** <script>alert(1)</script>", AiInsightTrendDto.Declining, AiInsightStatusDto.Done,
            null, "fake", null, null, "fingerprint", DateTimeOffset.UtcNow, null,
            [new(9, 1, 42, null, "Review tense", RecommendationSeverityDto.Major, "B2", null, false, DateTimeOffset.UtcNow)]);
        var panel = new AiInsightPanelViewModel("practice_session", 42, insight);
        Assert.True(panel.IsDone);
        Assert.False(panel.IsPending);
        Assert.False(panel.IsFailed);
        Assert.Equal("done", panel.Status);
        Assert.Equal("badge--danger", panel.TrendBadge);
        Assert.Equal("Trend: Declining", panel.TrendLabel);
        Assert.Equal("badge--danger", panel.RecommendationBadges[9]);
        Assert.Equal("Priority", panel.RecommendationLabels[9]);
        Assert.Contains("<strong>Review</strong>", panel.FormattedNarrative);
        Assert.DoesNotContain("<script>", panel.FormattedNarrative);
        Assert.True(new AiInsightPanelViewModel("dashboard", 0).IsPending);
    }

    /// <summary>
    /// #235 / PLC-61: the highlighted coordinates are a hint, and the model reduces them to at most the
    /// three the service sent. The input that would pass while being wrong is a page that lists every
    /// open step: that is a plan, not a highlight.
    /// </summary>
    [Fact]
    public void The_free_drill_priorities_are_labels_not_a_second_plan()
    {
        var catalog = new PracticeCatalogDto(
            new CertificateOptionDto("DEMO", "DEMO"), [], [], [], null);

        var model = new PracticeCatalogViewModel(catalog)
        {
            FreeDrill = new FreeDrillStateDto(
                IsUnlocked: true,
                IncompleteSteps: [],
                RemainingMinutes: 0,
                Priorities:
                [
                    new PrioritisedCoordinateDto(1, "READ", "Reading", ModalityDto.Reading, 5, "R-DET", "Detail", "B1", "w"),
                    new PrioritisedCoordinateDto(1, "READ", "Reading", ModalityDto.Reading, 6, "R-INF", "Inference", "B2", "w"),
                    new PrioritisedCoordinateDto(1, "READ", "Reading", ModalityDto.Reading, 7, "R-GRAM", "Grammar", "B1", "w"),
                ],
                CellUsedToday: false,
                DailyCeilingReached: false,
                AnsweredToday: 10,
                DailyItemCeiling: 60,
                LockNotice: null),
        };

        Assert.Null(model.FreeDrillLockNotice);
        Assert.Equal(["R-DET · B1", "R-INF · B2", "R-GRAM · B1"], model.PriorityLabels);
    }

    /// <summary>
    /// #235: an unlocked state from an older client (null FreeDrill) must still render — the page says
    /// nothing about a lock it was not told about, rather than inventing one.
    /// </summary>
    [Fact]
    public void A_catalog_without_a_free_drill_state_renders_no_lock_and_no_priorities()
    {
        var model = new PracticeCatalogViewModel(new PracticeCatalogDto(
            new CertificateOptionDto("DEMO", "DEMO"), [], [], [], null));

        Assert.Null(model.FreeDrill);
        Assert.Null(model.FreeDrillLockNotice);
        Assert.Empty(model.PriorityLabels);
    }
}

