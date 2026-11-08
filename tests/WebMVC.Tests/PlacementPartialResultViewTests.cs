using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #217 / PLC-19, PLC-35: what the result page prints for a partial result.
/// <para>
/// <b>The input that would pass a weaker test.</b> "The page renders" passes on a page that prints
/// `A1` for a skill whose grading failed, and on a page that silently omits the unmeasured skill so the
/// result looks complete. So each status is asserted through the column that shows it: a
/// <c>not_measured</c> row reads "Not measured" with its reason and never a level, a <c>grading</c> row
/// reads "Grading" and not a failure, and the retry is offered exactly when a task's grading failed.
/// </para>
/// </summary>
public sealed class PlacementPartialResultViewTests
{
    [Fact]
    public void A_not_measured_skill_prints_words_not_a_level()
    {
        var model = Model(
            Skill("READ", status: "not_measured", level: null, failureReason: "candidate_skipped"));

        var reading = model.SkillResults.Single();

        Assert.Equal("Not measured", model.LevelCell(reading));
        Assert.DoesNotContain("A1", model.LevelCell(reading));
        Assert.DoesNotContain("0", model.LevelCell(reading));
        Assert.Equal("You did not attempt this skill.", PlacementResultViewModel.NotMeasuredReason(reading.FailureReason));
    }

    [Theory]
    [InlineData("candidate_skipped", "You did not attempt this skill.")]
    [InlineData("inactivity_timeout", "The sitting ended before this skill was reached.")]
    [InlineData("ai_grading_failed", "The grader could not score this task.")]
    public void Each_failure_reason_is_explained_in_the_learners_words(string code, string expected) =>
        Assert.Equal(expected, PlacementResultViewModel.NotMeasuredReason(code));

    /// <summary>A grading task says it is still running - a wait, never a failure.</summary>
    [Fact]
    public void A_grading_skill_reads_grading_and_is_not_a_failure()
    {
        var model = Model(Skill("WRIT", status: "grading", level: null));

        var writing = model.SkillResults.Single();

        Assert.True(PlacementResultViewModel.IsGrading(writing));
        Assert.Equal("Grading", model.LevelCell(writing));
        Assert.Equal("Nothing was measured for this skill.", PlacementResultViewModel.NotMeasuredReason(null));
        // and it must not offer the retry: a job in the queue is not a failure
        Assert.False(model.CanRetryGrading);
    }

    /// <summary>The retry appears exactly when a task's grading failed, and never otherwise.</summary>
    [Fact]
    public void The_retry_is_offered_only_when_a_grading_failed()
    {
        Assert.True(Model(Skill("WRIT", "not_measured", null, "ai_grading_failed")).CanRetryGrading);
        Assert.False(Model(Skill("READ", "not_measured", null, "candidate_skipped")).CanRetryGrading);
        Assert.False(Model(Skill("WRIT", "grading", null)).CanRetryGrading);
        Assert.False(Model(Skill("LIST", "measured", "B2")).CanRetryGrading);
    }

    /// <summary>
    /// A partial result is announced. The overall is the weakest MEASURED skill, so a page that says
    /// nothing about the missing skill leaves the learner reading an overall that silently omits it.
    /// </summary>
    [Fact]
    public void A_partial_result_is_announced_and_has_a_retry_url()
    {
        var model = Model(
            Skill("LIST", "measured", "B2"),
            Skill("READ", "not_measured", null, "candidate_skipped"));

        Assert.True(model.Result.SkillResults!.Any(s => s.Status == "not_measured"));
        Assert.Equal("/placement/result/42/retry", model.RetryUrl);

        var root = TestPathHelper.FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Placement", "Result.cshtml"));
        // the banner names the exclusion in words, and the retry form posts to the model's URL
        Assert.Contains("This is a partial result", view);
        Assert.Contains("left out of your overall", view);
        Assert.Contains("Model.RetryUrl", view);
        Assert.Contains("Html.AntiForgeryToken()", view);
        Assert.Contains("Retrying is free", view);
    }

    private static PlacementResultViewModel Model(params PlacementSkillResultInfoDto[] results) =>
        new(new PlacementResultViewDto(
            PlacementId: 42, AttemptId: 100, CertId: 1, CertCode: "DEMO", ExamName: "Demo",
            TakenAt: DateTimeOffset.UnixEpoch, DurationMinutes: 30, TotalQuestions: 4,
            TotalScore: 200, ResultBandCode: "B1", ResultBandName: "Intermediate",
            CefrBands: [], SkillScores: [], WeakerSkillName: null, BandNote: null,
            IsGraded: false,
            SkillResults: [.. results]))
        {
            Dates = null!,
            SkillResults = [.. results],
            RetryUrl = "/placement/result/42/retry",
        };

    private static PlacementSkillResultInfoDto Skill(
        string code, string status, string? level, string? failureReason = null) =>
        new(1, code, code, status, level, level is null ? null : 50,
            level is null ? null : DateTimeOffset.UnixEpoch, ReusedFromPrior: false, failureReason);
}
