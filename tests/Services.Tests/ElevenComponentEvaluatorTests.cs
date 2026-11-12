using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class ElevenComponentEvaluatorTests
{
    /// <summary>
    /// #269: the durable rules <c>EvaluateObjectiveAnswer</c> applied, now asserted on a pinned version.
    /// <para>
    /// The legacy evaluator read a <c>Question</c> and its <c>QuestionOption</c> rows; both are gone, and
    /// the pool grades a blank through <see cref="AttemptEngine.EvaluatePinnedText"/>. These are the
    /// assertions that survived the move, unchanged in substance: case and whitespace do not decide an
    /// answer, the word ceiling is strict, a digit is refused when the author forbade one, a required
    /// keyword must be present, and a keyed option's text counts as an accepted answer.
    /// </para>
    /// <para>
    /// The label-shaped half - an mcq is right only when the chosen label is the key, a multi_select keyed
    /// A,B needs both - is no longer expressible as a pure function: <c>IsKeyLabel</c> is internal to
    /// <c>Services</c>. It is asserted through the sitting path instead, by
    /// <c>PoolOnlyPaperTests.A_paper_with_no_question_rows_is_sat_from_the_roster</c> (key A is right)
    /// and <c>PoolDrillGradingTests.A_multi_select_is_right_only_when_the_whole_label_set_is_chosen</c>
    /// (both labels right, one label wrong, an extra label wrong).
    /// </para>
    /// </summary>
    [Fact]
    public void A_pool_blank_is_graded_by_the_rules_the_legacy_evaluator_applied()
    {
        // Case and surrounding whitespace: an accepted answer is right however it was typed.
        var blank = new ItemVersion
        {
            ItemId = 1,
            VersionNumber = 1,
            Stem = "The team built a new ____.",
            AcceptedAnswersJson = "[\"laboratory\", \"lab\"]",
        };
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "laboratory"));
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "LABORATORY"));
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "Lab"));
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "   laboratory   "));

        // Anything else is wrong, including an empty and an absent answer.
        Assert.False(AttemptEngine.EvaluatePinnedText(blank, "classroom"));
        Assert.False(AttemptEngine.EvaluatePinnedText(blank, ""));
        Assert.False(AttemptEngine.EvaluatePinnedText(blank, null));

        // A keyed option's text is an accepted answer too, and only the keyed one.
        var keyed = new ItemVersion
        {
            ItemId = 2,
            VersionNumber = 1,
            Stem = "It was built in ____.",
            OptionA = "Sydney Opera House",
            OptionB = "Wrong Answer",
            OptionC = "Third",
            OptionD = "Fourth",
            KeyLabel = "A",
        };
        Assert.True(AttemptEngine.EvaluatePinnedText(keyed, "Sydney Opera House"));
        Assert.True(AttemptEngine.EvaluatePinnedText(keyed, "sydney opera house"));
        Assert.False(AttemptEngine.EvaluatePinnedText(keyed, "Wrong Answer"));

        // The word ceiling is strict: a matching answer over the limit is still wrong.
        var bounded = new ItemVersion
        {
            ItemId = 3,
            VersionNumber = 1,
            Stem = "Complete: ____.",
            MaxWords = 2,
            AcceptedAnswersJson = "[\"three blue cars\", \"blue cars\"]",
        };
        Assert.True(AttemptEngine.EvaluatePinnedText(bounded, "blue cars"));
        Assert.False(AttemptEngine.EvaluatePinnedText(bounded, "three blue cars"));
    }

    [Fact]
    public void A_blank_forbidding_digits_refuses_a_numeric_answer()
    {
        var blank = new ItemVersion
        {
            ItemId = 4,
            VersionNumber = 1,
            Stem = "The lecture is held in ____.",
            AllowNumber = false,
            AcceptedAnswersJson = "[\"room 101\", \"room one hundred\"]",
        };

        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "room one hundred"));
        Assert.False(AttemptEngine.EvaluatePinnedText(blank, "room 101"));
    }

    [Fact]
    public void A_blank_declaring_required_keywords_refuses_an_answer_without_them()
    {
        var blank = new ItemVersion
        {
            ItemId = 5,
            VersionNumber = 1,
            Stem = "Describe what you built in one clause.",
            RequiredKeywords = "[\"develop\", \"system\"]",
            AcceptedAnswersJson = "[\"developing a computer system\", \"system developer\"]",
        };

        // Both keyword stems are present, by lemmatisation ("developing" carries "develop").
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "developing a computer system"));
        Assert.True(AttemptEngine.EvaluatePinnedText(blank, "system developer"));

        // One keyword missing: wrong, even though the answer is on the accepted list.
        var missing = new ItemVersion
        {
            ItemId = 6,
            VersionNumber = 1,
            Stem = "Describe what you built in one clause.",
            RequiredKeywords = "develop, system",
            AcceptedAnswersJson = "[\"a computer system\"]",
        };
        Assert.False(AttemptEngine.EvaluatePinnedText(missing, "a computer system"));
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

        // #171: `independent` is gone. It returned 0 for every attempt while the trigger that
        // checks a total against its skills exempted it, so a certificate could be authored whose
        // learners could never be scored. A stored value that predates the CHECK must fail loudly
        // rather than be scored as zero: 0 is a real score, and a learner cannot tell them apart.
        var removed = Assert.Throws<ScoreConversionException>(
            () => AttemptEngine.AggregateTotalScore("independent", scores));
        Assert.Equal(ScoreConversionException.AggregationRuleUnknown, removed.Code);
        Assert.Contains("independent", removed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_aggregation_rule_throws_instead_of_falling_back_to_a_sum()
    {
        // Before #171 the switch's default arm was "sum", so any unrecognised value silently scored
        // as a sum. Naming the three rules explicitly means a fourth value can only be a bug.
        var scores = new List<AttemptSkillScore> { new() { SkillId = 1, ScaledScore = 60 } };

        var unknown = Assert.Throws<ScoreConversionException>(
            () => AttemptEngine.AggregateTotalScore("weighted_geometric", scores));
        Assert.Equal(ScoreConversionException.AggregationRuleUnknown, unknown.Code);

        // An absent rule is still the schema default, which is `sum`.
        Assert.Equal(60, AttemptEngine.AggregateTotalScore(null, scores));
        Assert.Equal(60, AttemptEngine.AggregateTotalScore("", scores));
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

        // E1.1 (#146): the scale is the skill's data, so the caller supplies it like the DB would.
        var result = service.CalculateScorePreview(
            request,
            bands: null,
            skillRanges: new Dictionary<int, (int Min, int Max, int Step)>
            {
                [1] = (10, 90, 5),
                [2] = (10, 90, 5)
            });

        // Writing skill: 65
        Assert.Equal(65, result.SkillScores.Single(s => s.SkillCode == "WRIT").ScaledScore);
        // Reading skill: 65
        Assert.Equal(65, result.SkillScores.Single(s => s.SkillCode == "READ").ScaledScore);

        // Overall average: (65 + 65) / 2 = 65
        Assert.Equal(65, result.TotalScore);
        Assert.Equal("BAND_6.5", result.BandCode);
        Assert.Equal("Competent Plus", result.BandName);
    }


    [Fact]
    public void EvaluateWritingResponse_Flags_UnderLength_When_Below_MinWords()
    {
        // IELTS Task 1: MinWords = 150. #269: the overload takes the two facts it reads - the word window
        // and the keywords - instead of a Question row that no longer exists.
        var shortEssay = string.Join(" ", Enumerable.Repeat("word", 100));
        var evalShort = AttemptEngine.EvaluateWritingResponse(150, null, shortEssay);
        Assert.Equal(100, evalShort.WordCount);
        Assert.True(evalShort.IsUnderLength);
        Assert.NotNull(evalShort.Feedback);
        Assert.Contains("IsUnderLength=true", evalShort.Feedback);
        Assert.Contains("MinWords=150", evalShort.Feedback);

        // 160 words -> Not under length
        var adequateEssay = string.Join(" ", Enumerable.Repeat("word", 160));
        var evalAdequate = AttemptEngine.EvaluateWritingResponse(150, null, adequateEssay);
        Assert.Equal(160, evalAdequate.WordCount);
        Assert.False(evalAdequate.IsUnderLength);

        // IELTS Task 2: MinWords = 250
        var task2Short = string.Join(" ", Enumerable.Repeat("word", 240));
        var evalTask2Short = AttemptEngine.EvaluateWritingResponse(250, null, task2Short);
        Assert.True(evalTask2Short.IsUnderLength);

        var task2Pass = string.Join(" ", Enumerable.Repeat("word", 260));
        var evalTask2Pass = AttemptEngine.EvaluateWritingResponse(250, null, task2Pass);
        Assert.False(evalTask2Pass.IsUnderLength);
    }

    [Fact]
    public void EvaluateWritingResponse_Detects_RequiredKeywords_With_Lemmatization()
    {
        // Contains lemma forms: "photographed" and "yesterday"
        var validResponse = "We took several photographed memories yesterday in the park.";
        var evalValid = AttemptEngine.EvaluateWritingResponse(10, "photograph, yesterday", validResponse);
        Assert.True(evalValid.HasRequiredKeywords);
        Assert.Empty(evalValid.MissingKeywords);

        // Missing "photograph"
        var invalidResponse = "We went to the park yesterday afternoon.";
        var evalInvalid = AttemptEngine.EvaluateWritingResponse(10, "photograph, yesterday", invalidResponse);
        Assert.False(evalInvalid.HasRequiredKeywords);
        Assert.Single(evalInvalid.MissingKeywords);
        Assert.Equal("photograph", evalInvalid.MissingKeywords[0]);
        Assert.Contains("MissingRequiredKeywords=[photograph]", evalInvalid.Feedback);

        // Suffix stripping lemmas
        Assert.True(AttemptEngine.ContainsKeywordStem("developing software is fun", "develop"));
        Assert.True(AttemptEngine.ContainsKeywordStem("project management meeting", "manage"));
        Assert.True(AttemptEngine.ContainsKeywordStem("medical examinations conducted", "examine"));
    }
}
