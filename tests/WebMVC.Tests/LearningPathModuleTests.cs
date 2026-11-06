using System.IO;
using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #232: the module-detail screen is gone with the template scaffolding it rendered. What the path
/// screen shows instead is one step's coordinate, and these tests pin the four metadata columns D21a
/// requires — sequence, topic, level, and the exact size with its minutes — plus the copy rules that
/// make a step readable without a raw code reaching the page.
/// </summary>
public sealed class LearningPathModuleTests
{
    private static PathStepDto Step(
        PathStepTypeDto stepType = PathStepTypeDto.FocusPractice,
        ModalityDto modality = ModalityDto.Reading,
        int? itemCount = 8,
        int? minutes = 12) =>
        new(
            StepId: 5,
            WeekNumber: 2,
            WeekSequence: 3,
            StepType: stepType,
            Route: StepRouteDto.ItemSelector,
            Title: "R-DET focus practice",
            Status: LearnerPathStepStatusDto.Available,
            SkillCode: "READ",
            SkillName: "Reading",
            Modality: modality,
            TopicCode: "R-DET",
            TopicName: "Local factual retrieval",
            TargetCefr: "B1",
            SizingTier: SizingTierDto.Developing,
            PlannedItemCount: itemCount,
            EstimatedMinutes: minutes,
            Score: null,
            ActualAccuracyPct: null,
            TargetAccuracyPct: 80,
            CanStart: true,
            StartedAt: null,
            CompletedAt: null,
            RefLessonId: null,
            RefExamId: null);

    /// <summary>
    /// D21a: a step shows its size before it starts — the exact item count with estimated minutes. The
    /// count and the minutes are both stated, and the unit matches the modulus.
    /// </summary>
    [Fact]
    public void A_step_states_its_exact_item_count_and_minutes()
    {
        var label = LearningPathIndexViewModel.SizeLabel(Step(itemCount: 8, minutes: 12));

        Assert.Equal("8 items · ~12 min", label);
    }

    /// <summary>A productive task is counted in tasks. "5 items" for one speaking prompt is a number
    /// with no meaning, and the label is the only place the unit appears.</summary>
    [Theory]
    [InlineData(ModalityDto.Speaking, 1, "1 task · ~20 min")]
    [InlineData(ModalityDto.Writing, 2, "2 tasks · ~20 min")]
    [InlineData(ModalityDto.Reading, 1, "1 item · ~20 min")]
    [InlineData(ModalityDto.Listening, 5, "5 items · ~20 min")]
    public void A_productive_step_is_counted_in_tasks_not_items(
        ModalityDto modality, int count, string expected)
    {
        Assert.Equal(expected, LearningPathIndexViewModel.SizeLabel(Step(modality: modality, itemCount: count, minutes: 20)));
    }

    /// <summary>
    /// Trap: a lesson or a mock section with no item count rendered as "0 items". It states its minutes
    /// alone, because a count it does not have is a number it cannot back.
    /// </summary>
    [Fact]
    public void A_step_with_no_item_count_states_only_its_minutes()
    {
        Assert.Equal("~15 min", LearningPathIndexViewModel.SizeLabel(Step(itemCount: null, minutes: 15)));
    }

    /// <summary>A step with neither is a dash, never a fabricated zero.</summary>
    [Fact]
    public void A_step_with_no_size_at_all_shows_a_dash()
    {
        Assert.Equal("—", LearningPathIndexViewModel.SizeLabel(Step(itemCount: null, minutes: null)));
    }

    /// <summary>
    /// D19: every step type maps to words. Trap: a fallback that prints the raw enum name — "FocusPractice"
    /// in a placard is the unmapped code D19 refuses.
    /// </summary>
    [Theory]
    [InlineData(PathStepTypeDto.DiagnosticDrill)]
    [InlineData(PathStepTypeDto.FocusPractice)]
    [InlineData(PathStepTypeDto.ReviewSpaced)]
    [InlineData(PathStepTypeDto.ConceptLesson)]
    [InlineData(PathStepTypeDto.ExpandedRemedial)]
    [InlineData(PathStepTypeDto.MockSection)]
    public void Every_step_type_has_words_of_its_own(PathStepTypeDto stepType)
    {
        var label = LearningPathIndexViewModel.StepTypeLabel(stepType);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(stepType.ToString(), label);
        // A label is prose, not a code: no underscores, no camel-case run-together.
        Assert.DoesNotContain("_", label);
    }

    /// <summary>
    /// The step screen exists and states the coordinate. The old module-detail view was deleted with the
    /// scaffolding, so a view that still asked for a module heading would be a screen with no data.
    /// </summary>
    [Fact]
    public void The_step_view_states_the_four_metadata_columns()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "LearningPath", "Step.cshtml");

        Assert.True(File.Exists(viewPath), "Step.cshtml must exist");
        var content = File.ReadAllText(viewPath);

        Assert.Contains("SEQUENCE", content);
        Assert.Contains("TOPIC", content);
        Assert.Contains("LEVEL", content);
        Assert.Contains("SIZE", content);
        Assert.Matches(@"@Model\.TargetCefr", content);
        Assert.Matches(@"@Model\.TopicCode", content);
    }
}
