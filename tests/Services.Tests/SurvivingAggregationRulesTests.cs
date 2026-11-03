using BusinessObjects.Entities;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #171 acceptance: "A certificate created with each of the three remaining rules still scores exactly
/// as before." Removing an enum value is a change to the values that stay, so the expected numbers here
/// are the pre-change ones, transcribed from the old switch (not recomputed from the new code):
/// <code>
///   "avg_half_step" => RoundHalfStep(average)
///   "avg_whole"     => (int)Math.Round(average, MidpointRounding.AwayFromZero)
///   _               => skillScores.Sum(...)          // which was and is the `sum` arm
/// </code>
/// Each row is a boundary of one of the three rules, so a change to any rounding step shows up here.
/// </summary>
public sealed class SurvivingAggregationRulesTests
{
    private static IReadOnlyCollection<AttemptSkillScore> Scores(params int[] scaled) =>
        scaled.Select((s, i) => new AttemptSkillScore { SkillId = i + 1, ScaledScore = s }).ToList();

    /// <summary>Pre-change expected values for <c>sum</c>: plain addition, no rounding, no scaling.</summary>
    [Theory]
    [InlineData(200, 300, 500)]        // TOEIC L&amp;R, the seeded certificate
    [InlineData(160, 170, 330)]        // TOEIC S&amp;W's two 0-200 scales
    [InlineData(0, 0, 0)]              // zero is a real score, not an error
    [InlineData(495, 495, 990)]        // the top of the TOEIC scale
    [InlineData(7, 0, 7)]              // one empty skill does not silently shrink a sum
    public void Sum_adds_the_skill_scores_unchanged(int a, int b, int expected) =>
        Assert.Equal(expected, AttemptEngine.AggregateTotalScore("sum", Scores(a, b)));

    /// <summary>
    /// Pre-change expected values for <c>avg_half_step</c>, on the tenths values IELTS and VSTEP store
    /// (60 = 6.0, 65 = 6.5). The boundaries are the ones RoundHalfStep documents: below .25 rounds down,
    /// .25-.75 to the half, .75 and up to the next whole.
    /// </summary>
    [Theory]
    [InlineData(60, 60, 60)]      // 6.0 exactly
    [InlineData(60, 65, 65)]      // 6.25 -> 6.5
    [InlineData(62, 62, 60)]      // 6.2 -> 6.0, below the .25 boundary
    [InlineData(63, 63, 65)]      // 6.3 -> 6.5, at or above .25
    [InlineData(67, 67, 65)]      // 6.7 -> 6.5, below the .75 boundary
    [InlineData(68, 68, 70)]      // 6.8 -> 7.0, at or above .75
    [InlineData(70, 70, 70)]      // 7.0 exactly
    [InlineData(40, 90, 65)]      // 4.0 and 9.0 -> 6.5
    public void Avg_half_step_rounds_to_the_official_half_band(int a, int b, int expected) =>
        Assert.Equal(expected, AttemptEngine.AggregateTotalScore("avg_half_step", Scores(a, b)));

    /// <summary>Pre-change expected values for <c>avg_whole</c>: away from zero at .5.</summary>
    [Theory]
    [InlineData(50, 60, 55)]      // 55.0 exactly
    [InlineData(50, 61, 56)]      // 55.5 -> 56, away from zero
    [InlineData(50, 51, 51)]      // 50.5 -> 51
    [InlineData(10, 10, 10)]      // 10.0
    [InlineData(4, 5, 5)]         // 4.5 -> 5 (VSTEP's four-skills shape)
    [InlineData(6, 7, 7)]         // 6.5 -> 7
    public void Avg_whole_rounds_away_from_zero_at_the_midpoint(int a, int b, int expected) =>
        Assert.Equal(expected, AttemptEngine.AggregateTotalScore("avg_whole", Scores(a, b)));

    /// <summary>
    /// The three rules must not collapse into each other. One input set whose three answers differ
    /// proves the switch still branches rather than returning the same arm for every rule.
    /// </summary>
    [Fact]
    public void The_three_rules_give_three_different_answers_on_the_same_scores()
    {
        var scores = Scores(60, 65);

        Assert.Equal(125, AttemptEngine.AggregateTotalScore("sum", scores));
        Assert.Equal(65, AttemptEngine.AggregateTotalScore("avg_half_step", scores));
        Assert.Equal(63, AttemptEngine.AggregateTotalScore("avg_whole", scores));
    }

    /// <summary>
    /// The rule is matched case-insensitively and tolerates surrounding whitespace, as before: the
    /// column is admin-authored text, and a trailing space used to score correctly.
    /// </summary>
    [Theory]
    [InlineData("SUM")]
    [InlineData("Sum")]
    [InlineData(" sum ")]
    public void The_rule_is_matched_ignoring_case_and_surrounding_space(string rule) =>
        Assert.Equal(125, AttemptEngine.AggregateTotalScore(rule, Scores(60, 65)));

    /// <summary>The certificate's default when no rule is given is still <c>sum</c>.</summary>
    [Fact]
    public void An_absent_rule_is_still_the_schema_default_sum()
    {
        var scores = Scores(60, 65);

        Assert.Equal(125, AttemptEngine.AggregateTotalScore(null, scores));
        Assert.Equal(125, AttemptEngine.AggregateTotalScore("", scores));
    }
}
