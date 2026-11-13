using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #152 AC3 in the place it failed: the review page. A 0-9 certificate stores its marks in tenths, so
/// a 7.5 is held as 75 and a page that prints the stored integer tells the learner "75 · out of 90".
/// The rendering contract is therefore: the view model, not the view, divides by the certificate's
/// factor — so this test asserts on the exact strings the page writes, and a units mistake anywhere
/// between the API and the page fails it.
/// </summary>
public sealed class SpeakingScoreDisplayTests
{
    private static SpeakingReviewDto Review(int? scaled, int? max, int factor, string certCode = "IELTS") => new(
        AttemptId: 1,
        ExamId: 2,
        ExamName: "Cambridge IELTS 18 - Speaking Practice Test 1",
        CertCode: certCode,
        SubmittedAt: DateTimeOffset.UnixEpoch,
        TotalQuestions: 2,
        AnsweredQuestions: 2,
        TotalScore: 5.4m,
        MaxScore: 6.0m,
        ScaledScore: scaled,
        Questions: [],
        LowestScoredQuestion: null,
        EstimatedBandCode: "C1",
        EstimatedBandName: "Advanced (7.0-8.0)",
        ScaledScoreMax: max,
        ScoreDisplayFactor: factor);

    [Fact]
    public void A_tenths_score_is_rendered_as_the_band_score_not_the_stored_integer()
    {
        var model = new SpeakingReviewViewModel(Review(scaled: 75, max: 90, factor: 10));

        Assert.Equal("7.5", model.ScoreDisplay);
        Assert.Equal("9", model.ScoreMaxDisplay);
        Assert.Equal("Speaking score · out of 9 · IELTS scale", model.ScoreScaleLabel);
        Assert.DoesNotContain("75", model.ScoreDisplay);
    }

    [Fact]
    public void A_whole_point_score_is_rendered_unchanged()
    {
        var model = new SpeakingReviewViewModel(Review(scaled: 160, max: 200, factor: 1, certCode: "TOEIC_SW"));

        Assert.Equal("160", model.ScoreDisplay);
        Assert.Equal("200", model.ScoreMaxDisplay);
        Assert.Equal("Speaking score · out of 200 · TOEIC_SW scale", model.ScoreScaleLabel);
    }

    [Fact]
    public void A_certificate_with_no_bands_shows_no_range_rather_than_an_invented_one()
    {
        var model = new SpeakingReviewViewModel(Review(scaled: null, max: null, factor: 1, certCode: "CUSTOM"));

        Assert.Equal("5.4", model.ScoreDisplay);
        Assert.Equal(string.Empty, model.ScoreMaxDisplay);
        Assert.Equal("Speaking score · CUSTOM scale", model.ScoreScaleLabel);
    }

    [Fact]
    public void A_mock_result_on_a_tenths_certificate_is_rendered_divided()
    {
        var dto = new AttemptResultDto(
            AttemptId: 1, ExamId: 2, ExamName: "Mock", CertId: 3, CertCode: "IELTS", CertName: "IELTS",
            Status: AttemptStatusDto.Graded,
            StartedAt: DateTimeOffset.UnixEpoch, SubmittedAt: DateTimeOffset.UnixEpoch,
            DurationSeconds: 600, IsAutoSubmitted: false,
            TotalScore: 65, BandCode: "B2", BandName: "Upper intermediate",
            SkillScores: [new AttemptSkillScoreDto(1, "LIST", "Listening", ModalityDto.Listening, 30, 65, 40)],
            PreviousAttemptScore: 60, ScoreDelta: 5,
            ScoreDisplayFactor: 10);

        // The mapper divides once; these are the values it hands the page.
        Assert.Equal("6.5", ScoreDisplay.Format(dto.TotalScore, dto.ScoreDisplayFactor));
        Assert.Equal("+0.5", "+" + ScoreDisplay.Format(dto.ScoreDelta!.Value, dto.ScoreDisplayFactor));
        Assert.Equal("6.5", new ExamSkillScoreViewModel(dto.SkillScores[0], 75, dto.ScoreDisplayFactor).ScaledScoreDisplay);
    }

    [Fact]
    public void A_placement_result_on_a_tenths_certificate_is_rendered_divided()
    {
        var dto = new PlacementResultViewDto(
            PlacementId: 1, AttemptId: 2, CertId: 3, CertCode: "IELTS", ExamName: "Placement",
            TakenAt: DateTimeOffset.UnixEpoch, DurationMinutes: 60, TotalQuestions: 40,
            TotalScore: 65, ResultBandCode: "B2", ResultBandName: "Upper intermediate",
            CefrBands: [new CefrBandInfoDto("B2", "B2", 55, 65, true, true, false)],
            SkillScores: [new PlacementSkillScoreInfoDto(1, "LIST", "Listening", 30, 40, 65, "B2")],
            WeakerSkillName: null, BandNote: null,
            ScoreDisplayFactor: 10);

        var model = new PlacementResultViewModel(dto);

        Assert.Equal("6.5", model.SkillScoreDisplay(dto.SkillScores[0]));
        Assert.Equal("6.5", ScoreDisplay.Format(dto.TotalScore, dto.ScoreDisplayFactor));
        Assert.Equal("6.5", ScoreDisplay.Format(dto.CefrBands.Max(b => b.MaxTotal), dto.ScoreDisplayFactor));
    }

    /// <summary>
    /// #152 finding 3: the service's reported score and the band the grading worker stored are one
    /// placement, and that placement lives beside the engine's other scale rules rather than inside a
    /// practice service. Services.Tests drives the behaviour against a real database; this is the
    /// cheap structural half, because a second copy of the rule is what the finding was about.
    /// </summary>
    [Fact]
    public void One_axis_decides_both_the_score_and_the_band_in_one_place()
    {
        var root = TestPathHelper.FindRepoRoot();
        var scoring = File.ReadAllText(Path.Combine(root, "src", "Services", "Attempts", "AttemptEngine.Scoring.cs"));
        Assert.Contains("PlaceRubricAsync", scoring);
        Assert.Contains("RubricAxisAsync", scoring);
        // The band estimate is the placement's own band, not a second walk over the bands.
        Assert.Contains("(await PlaceRubricAsync(", scoring);

        var speaking = File.ReadAllText(Path.Combine(root, "src", "Services", "SpeakingPracticeService.cs"));
        Assert.DoesNotContain("private static int GreatestCommonDivisor", speaking);
        Assert.DoesNotContain("private async Task<(int Min, int Max, int Step)?> RubricAxisAsync", speaking);
        Assert.Contains("AttemptEngine.PlaceRubricAsync", speaking);
    }

    /// <summary>
    /// #152 finding 8, decided and recorded in docs/STATE.md (D18): scripted questions keep the
    /// unscripted endpoint. The provider's own feature table gives IELTS Speaking band scores only to
    /// unscripted analysis, and those are the fields the certificate rubric reads — routing Read
    /// Aloud to <c>sent.eval.promax</c> would turn every criterion into "not measured", which this
    /// ticket makes a Failed grading. This pins the decision so a future "obvious" switch to a
    /// scripted coreType has to delete a test and a recorded reason, not just change a string.
    /// </summary>
    [Fact]
    public void The_core_type_stays_the_unscripted_endpoint_and_the_reason_is_recorded()
    {
        var root = TestPathHelper.FindRepoRoot();

        // The reason lives beside the setting, not only in a document nobody reads at the call site.
        var source = File.ReadAllText(Path.Combine(root, "src", "Services", "SpeechSuper", "SpeechSuperOptions.cs"));
        Assert.Contains("public string CoreType { get; set; } = \"speak.eval.pro\";", source);
        Assert.Contains("sent.eval.promax", source);
        Assert.Contains("IELTS Speaking band scores", source);

        // And the decision is in the decisions file.
        var state = File.ReadAllText(Path.Combine(root, "docs", "STATE.md"));
        Assert.Contains("| D18 |", state);
        Assert.Contains("sent.eval.promax", state);
    }
}
