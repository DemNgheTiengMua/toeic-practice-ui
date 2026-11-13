using System.Text.Json;
using Contracts;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #158: the admin preview reuses the learner's sitting view. These tests pin the two things that
/// make that reuse safe — the preview flags reach the shell, and a learner's sitting gets none of
/// them — plus the answer key the toggle reveals.
/// </summary>
public sealed class ExamPreviewViewTests
{
    private static AttemptTakeDto Take()
    {
        var q = new AttemptQuestionDto(
            501, 3, 20, 7, "P1", "Photographs", "LIST", "Listening", ModalityDto.Listening,
            "Look at the photograph.", null, null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "First"), new AttemptOptionDto(2, "B", "Second")],
            null, false);
        return new AttemptTakeDto(
            AttemptId: 0, ExamId: 42, "Draft exam", "TOEIC", AttemptStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 7200,
            20, 0, 0, q,
            [new NavigatorItemDto(501, 3, 7, "P1", ModalityDto.Listening, false, false, true, IsLocked: false)],
            false,
            Questions: [q],
            Phases: [new AttemptPhaseDto(1, "LIST", "Listening", 1, 0, 7200)],
            ActiveSkillId: 1);
    }

    private static ExamPreviewTakeDto Preview(AttemptTakeDto take) => new(
        take,
        [new ExamPreviewAnswerDto(501, [2], "B is the only grammatical fit.")],
        [
            new ExamPreviewSectionDto(7, "P1", "Photographs", 1, 10, 10),
            new ExamPreviewSectionDto(9, "P5", "Incomplete Sentences", 11, 20, 10)
        ],
        120,
        "draft");

    [Fact]
    public void A_preview_tells_the_shell_it_is_a_preview_and_the_sitting_does_not()
    {
        var preview = new ExamTakeViewModel(Take(), Preview: Preview(Take()));
        using var previewSitting = JsonDocument.Parse(preview.Display.SittingJson);
        Assert.True(previewSitting.RootElement.GetProperty("previewMode").GetBoolean(),
            "the shell must know it is a preview so it unlocks navigation and stays read-only");
        Assert.Equal(42, previewSitting.RootElement.GetProperty("examId").GetInt32());

        var sitting = new ExamTakeViewModel(Take());
        using var sittingData = JsonDocument.Parse(sitting.Display.SittingJson);
        Assert.False(sittingData.RootElement.GetProperty("previewMode").GetBoolean(),
            "a learner's sitting must never be treated as a preview");
        Assert.False(sitting.IsPreview);
    }

    [Fact]
    public void The_preview_placard_reports_the_exam_status_and_the_blueprint_duration()
    {
        var model = new ExamTakeViewModel(Take(), Preview: Preview(Take()));

        Assert.True(model.IsPreview);
        Assert.Equal("DRAFT", model.ExamStatus);
        Assert.Equal(120, model.DurationMinutes);
        Assert.Equal(20, model.Display.TotalQuestions);
        Assert.Equal(2, model.SectionChoices.Count);
        Assert.StartsWith("P1 · Photographs (Q1–10)", model.SectionChoices[0].Label);
        Assert.Equal(11, model.SectionChoices[1].DisplayOrder);
    }

    [Fact]
    public void The_answer_key_is_the_JSON_the_toggle_reads()
    {
        var model = new ExamTakeViewModel(Take(), Preview: Preview(Take()));

        using var answers = JsonDocument.Parse(model.PreviewAnswersJson);
        var entry = answers.RootElement.GetProperty("501");
        Assert.Equal(2, Assert.Single(entry.GetProperty("correctOptionIds").EnumerateArray()).GetInt32());
        Assert.Equal("B is the only grammatical fit.", entry.GetProperty("explanation").GetString());
    }

    [Fact]
    public void A_learner_sitting_carries_no_answer_key_at_all()
    {
        var model = new ExamTakeViewModel(Take());

        // Not "hidden in CSS": the key is absent from the page a learner receives, so there is
        // nothing to un-hide from devtools.
        Assert.Equal("{}", model.PreviewAnswersJson);
        Assert.Empty(model.SectionChoices);
        Assert.Equal(string.Empty, model.ExamStatus);
    }
}
