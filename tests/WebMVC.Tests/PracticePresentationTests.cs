using Contracts;
using System.Text.Json;
using WebMVC.Models;

namespace WebMVC.Tests;

public sealed class PracticePresentationTests
{
    private static PracticeSessionDto Session(PracticeStatusDto status = PracticeStatusDto.InProgress) =>
        new(42, 5, "P5", "Reading", ModalityDto.Reading, 3, status, 1,
            QuestionSourceDto.All, FeedbackModeDto.Immediate, DateTimeOffset.UtcNow, null);

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
    public void Deferred_feedback_counts_selected_answers_without_revealing_correctness()
    {
        var question = Question(1, 11);
        var model = new PracticeTakePresentation(Session() with { FeedbackMode = FeedbackModeDto.End },
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
}

