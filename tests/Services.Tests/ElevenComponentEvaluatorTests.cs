using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class ElevenComponentEvaluatorTests
{
    [Fact]
    public void Mcq_And_Mcq3_Evaluates_Correct_Option()
    {
        var qMcq = new Question { QuestionType = "mcq" };
        var qMcq3 = new Question { QuestionType = "mcq_3" };

        var options = new List<QuestionOption>
        {
            new() { OptionId = 1, Label = "A", Text = "Correct A", IsCorrect = true },
            new() { OptionId = 2, Label = "B", Text = "Wrong B", IsCorrect = false },
            new() { OptionId = 3, Label = "C", Text = "Wrong C", IsCorrect = false }
        };

        // Correct selection awards 1
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qMcq, options, 1, null, null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qMcq3, options, 1, null, null));

        // Wrong selection awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMcq, options, 2, null, null));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMcq3, options, 3, null, null));

        // Missing selection awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMcq, options, null, null, null));
    }

    [Fact]
    public void MultiSelect_Requires_Exact_Option_Set_Match()
    {
        var q = new Question { QuestionType = "multi_select", MaxSelections = 2 };

        var options = new List<QuestionOption>
        {
            new() { OptionId = 10, Label = "A", Text = "Option A", IsCorrect = true },
            new() { OptionId = 11, Label = "B", Text = "Option B", IsCorrect = false },
            new() { OptionId = 12, Label = "C", Text = "Option C", IsCorrect = true },
            new() { OptionId = 13, Label = "D", Text = "Option D", IsCorrect = false }
        };

        // Exact match [10, 12] awards 1
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "[10, 12]", null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "[12, 10]", null));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "10,12", null));

        // Partial match [10] awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "[10]", null));

        // Extra option [10, 11, 12] awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "[10, 11, 12]", null));

        // Empty selection awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, null));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, "[]", null));
    }

    [Fact]
    public void InlineCompletion_Whitespace_Normalization_And_Case_Insensitivity()
    {
        var q = new Question
        {
            QuestionType = "inline_completion",
            AcceptedAnswersJson = "[\"laboratory\", \"lab\"]",
            AllowNumber = true
        };

        var options = new List<QuestionOption>();

        // Exact match
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "laboratory"));

        // Case insensitivity
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "LABORATORY"));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "Lab"));

        // Whitespace trimming and multiple spaces collapse
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "   laboratory   "));

        // Incorrect text awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "classroom"));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, ""));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, null));
    }

    [Fact]
    public void InlineCompletion_Matches_Against_Correct_QuestionOptions()
    {
        var q = new Question
        {
            QuestionType = "inline_completion",
            AcceptedAnswersJson = null,
            AllowNumber = true
        };

        var options = new List<QuestionOption>
        {
            new() { OptionId = 1, Text = "Sydney Opera House", IsCorrect = true },
            new() { OptionId = 2, Text = "Wrong Answer", IsCorrect = false }
        };

        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "Sydney Opera House"));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "sydney opera house"));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "Wrong Answer"));
    }

    [Fact]
    public void InlineCompletion_Enforces_MaxWords_Strictly()
    {
        var q = new Question
        {
            QuestionType = "inline_completion",
            MaxWords = 2,
            AcceptedAnswersJson = "[\"three blue cars\", \"blue cars\"]",
            AllowNumber = true
        };

        var options = new List<QuestionOption>();

        // 2 words matches MaxWords limit
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "blue cars"));

        // 3 words exceeds MaxWords (2) -> awards 0 despite being in AcceptedAnswersJson
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "three blue cars"));
    }

    [Fact]
    public void InlineCompletion_Enforces_AllowNumber_False()
    {
        var q = new Question
        {
            QuestionType = "inline_completion",
            AllowNumber = false,
            AcceptedAnswersJson = "[\"room 101\", \"room one hundred\"]"
        };

        var options = new List<QuestionOption>();

        // Without numbers: valid
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "room one hundred"));

        // Contains digits when AllowNumber is false: awards 0
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "room 101"));
    }

    [Fact]
    public void InlineCompletion_Verifies_RequiredKeywords_Presence()
    {
        var q = new Question
        {
            QuestionType = "inline_completion",
            RequiredKeywords = "[\"develop\", \"system\"]",
            AcceptedAnswersJson = "[\"developing a computer system\", \"system developer\"]",
            AllowNumber = true
        };

        var options = new List<QuestionOption>();

        // Contains both keyword stems ("develop" and "system") -> awards 1
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "developing a computer system"));
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(q, options, null, null, "system developer"));

        // Missing "develop" keyword -> awards 0
        var qMissingKeyword = new Question
        {
            QuestionType = "inline_completion",
            RequiredKeywords = "develop, system",
            AcceptedAnswersJson = "[\"a computer system\"]"
        };
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qMissingKeyword, options, null, null, "a computer system"));
    }

    [Fact]
    public void Dropdown_And_DragDrop_Support_OptionId_Or_ResponseText()
    {
        var qDropdown = new Question { QuestionType = "dropdown" };
        var qDragDrop = new Question { QuestionType = "drag_drop", AcceptedAnswersJson = "[\"Category A\"]" };

        var options = new List<QuestionOption>
        {
            new() { OptionId = 5, Label = "A", Text = "Category A", IsCorrect = true },
            new() { OptionId = 6, Label = "B", Text = "Category B", IsCorrect = false }
        };

        // Match by SelectedOptionId
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDropdown, options, 5, null, null));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qDropdown, options, 6, null, null));

        // Match by ResponseText
        Assert.True(AttemptEngine.EvaluateObjectiveAnswer(qDragDrop, options, null, null, "category a"));
        Assert.False(AttemptEngine.EvaluateObjectiveAnswer(qDragDrop, options, null, null, "category b"));
    }

    [Theory]
    [InlineData(62.0, 60)]  // decimal 0.20 < 0.25 -> 6.0
    [InlineData(62.4, 60)]  // decimal 0.24 < 0.25 -> 6.0
    [InlineData(62.5, 65)]  // decimal 0.25 >= 0.25 & < 0.75 -> 6.5
    [InlineData(65.0, 65)]  // decimal 0.50 -> 6.5
    [InlineData(67.0, 65)]  // decimal 0.70 < 0.75 -> 6.5
    [InlineData(67.5, 70)]  // decimal 0.75 >= 0.75 -> 7.0
    [InlineData(68.0, 70)]  // decimal 0.80 >= 0.75 -> 7.0
    [InlineData(69.0, 70)]  // decimal 0.90 -> 7.0
    public void Ielts_Official_Half_Step_Rounding(double rawAverage, int expectedTotal)
    {
        Assert.Equal(expectedTotal, AttemptEngine.RoundHalfStep(rawAverage));
    }

    [Fact]
    public void ScoreAggregationRules_All_Variants_Work_Accurately()
    {
        var scores = new List<AttemptSkillScore>
        {
            new() { SkillId = 1, ScaledScore = 60 },
            new() { SkillId = 2, ScaledScore = 65 }
        };

        // sum: 60 + 65 = 125
        Assert.Equal(125, AttemptEngine.AggregateTotalScore("sum", scores));

        // avg_half_step: average = 62.5 -> 65
        Assert.Equal(65, AttemptEngine.AggregateTotalScore("avg_half_step", scores));

        // avg_whole: average = 62.5 -> 63
        Assert.Equal(63, AttemptEngine.AggregateTotalScore("avg_whole", scores));

        // independent: 0
        Assert.Equal(0, AttemptEngine.AggregateTotalScore("independent", scores));
    }

    [Fact]
    public void CalculateScorePreview_With_Section_Weights_And_Live_Calculations()
    {
        var service = new ScoreConversionService(null!);

        // IELTS Writing scenario: Task 1 weight 1.0, Task 2 weight 2.0
        // Task 1: 60 (Band 6.0), Task 2: 70 (Band 7.0)
        // Weighted section average = (60 * 1.0 + 70 * 2.0) / 3.0 = 66.67
        // Half-step rounded = 65 (Band 6.5)
        var request = new ScorePreviewRequest(
            CertId: null,
            CertCode: "IELTS",
            ScoreAggregationRule: "avg_half_step",
            Skills:
            [
                new SkillScoreInput(
                    SkillId: 1,
                    SkillCode: "WRIT",
                    RawScore: null,
                    Sections:
                    [
                        new SectionScoreInput(1, "Task1", 1, 60m, ScoreWeight: 1.0m),
                        new SectionScoreInput(2, "Task2", 1, 70m, ScoreWeight: 2.0m)
                    ]),
                new SkillScoreInput(
                    SkillId: 2,
                    SkillCode: "READ",
                    RawScore: 65m,
                    Sections: null)
            ],
            CustomBands:
            [
                new UpdateScoreBandEntry(1, "BAND_6", "Competent", 60, 64, 1),
                new UpdateScoreBandEntry(2, "BAND_6.5", "Competent Plus", 65, 69, 2),
                new UpdateScoreBandEntry(3, "BAND_7", "Good User", 70, 74, 3)
            ]);

        var result = service.CalculateScorePreview(request);

        // Writing skill: 65
        Assert.Equal(65, result.SkillScores.Single(s => s.SkillCode == "WRIT").ScaledScore);
        // Reading skill: 65
        Assert.Equal(65, result.SkillScores.Single(s => s.SkillCode == "READ").ScaledScore);

        // Overall average: (65 + 65) / 2 = 65
        Assert.Equal(65, result.TotalScore);
        Assert.Equal("BAND_6.5", result.BandCode);
        Assert.Equal("Competent Plus", result.BandName);
    }
}
