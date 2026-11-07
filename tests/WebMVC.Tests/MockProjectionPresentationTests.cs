using System;
using System.IO;
using System.Linq;
using Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Mappers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #222 / PRN-03: the mock result page shows the certificate score and, separately, the projected level,
/// and each is labelled as what it is.
/// <para>
/// These are presentation guards because the defect they cover is pure copy: every one of them passes
/// with correct data and a wrong sentence. The assertions therefore test the words the learner reads and
/// the shape of the markup, not the numbers behind them (the service tests own those).
/// </para>
/// </summary>
public sealed class MockProjectionPresentationTests
{
    private static string View() =>
        File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Exams", "Result.cshtml"));

    // =============================================================================================
    // AC1 / AC3: two quantities, each labelled as what it is.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> the page titles the certificate total
    /// "Proficiency Level / Score Band" - the exact wording the old page used, and a band is not a level
    /// (PRN-03). The assertion is that neither the score label nor the band card calls itself a level.
    /// </summary>
    [Fact]
    public void The_certificate_score_and_the_band_are_not_labelled_a_level()
    {
        var view = View();

        Assert.DoesNotContain("Total scaled score", view, StringComparison.Ordinal);
        Assert.Contains("Projected level from this paper", view, StringComparison.Ordinal);
        Assert.Contains("Band on this certificate's own scale", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Proficiency Level / Score Band", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// The score hero's own label comes from the mapper, and it must say "certificate score" rather than
    /// anything that could be read as a CEFR level.
    /// </summary>
    [Fact]
    public void The_score_label_reads_certificate_score()
    {
        var result = new AttemptResultDto(
            1, 1, "Mock", 1, "TOEIC", "TOEIC", AttemptStatusDto.Graded,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, 7200, false,
            750, "B2", "B2 band", [], null, null);

        var model = AttemptViewModelMapper.Result(result, Format(), null, null);

        Assert.Contains("Certificate score", model.TotalScoreLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("level", model.TotalScoreLabel, StringComparison.OrdinalIgnoreCase);
    }

    private static Formatters.DateTimeFormatter Format() =>
        new(new Microsoft.AspNetCore.Http.HttpContextAccessor());

    // =============================================================================================
    // AC3, the copy defect: no band code is ever rendered as a level.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a band code "B2" on a TOEIC paper that
    /// found its way into the level badge, so the page says the learner is CEFR B2 because the band is
    /// called B2. Here the projection is cannot-judge while the band is "B2": the page must say the score
    /// sits between bands and must not print a level at all.
    /// </summary>
    [Fact]
    public void A_paper_with_no_projected_level_names_the_reason_and_shows_no_level()
    {
        var dto = Projection(
            bandCode: "B2",
            skills: [new MockSkillProjectionDto(1, 350, null, null, MockProjectionOutcomeDto.CannotJudge, "no_cut_score_row_for_level")],
            cannotJudgeReason: "no_cut_score_row_for_level");

        var model = MockProjectionViewModelMapper.Map(dto, Scores(), 1, NullLogger.Instance);

        Assert.True(model.Available);
        Assert.False(model.SyncOffered);
        Assert.Contains("between two bands", model.CannotJudgeText, StringComparison.Ordinal);
        Assert.Null(Assert.Single(model.Skills).LevelLabel);
        Assert.DoesNotContain(model.Skills, s => s.LevelLabel is not null);
    }

    /// <summary>
    /// The score→level lookup must never fall back to a band: a band code that happens to look like a
    /// level ("B2") in <c>BandCode</c> produces no level on the placard when the cut table refused.
    /// </summary>
    [Fact]
    public void A_band_code_that_looks_like_a_level_does_not_become_the_projected_level()
    {
        var dto = Projection(
            bandCode: "B2",
            skills: [new MockSkillProjectionDto(1, 350, null, null, MockProjectionOutcomeDto.CannotJudge, "no_cut_score_row_for_level")],
            cannotJudgeReason: "no_cut_score_row_for_level");

        var model = MockProjectionViewModelMapper.Map(dto, Scores(), 1, NullLogger.Instance);

        Assert.DoesNotContain("B2", model.Skills.SelectMany(s => new[] { s.LevelLabel, s.OutcomeLabel }), StringComparer.Ordinal);
    }

    // =============================================================================================
    // AC2: the sync is an explicit form POST carrying anti-forgery.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a sync wired as a GET link, or an AJAX
    /// call that skips the token. Either writes the learner's state from a page load rather than a click.
    /// </summary>
    [Fact]
    public void The_sync_is_a_post_form_with_an_antiforgery_token()
    {
        var view = View();

        Assert.Contains("method=\"post\"", view, StringComparison.Ordinal);
        Assert.Contains("/projection/sync", view, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> rendering the sync form on every result,
    /// including one where nothing differs. The offer is driven by the API's own comparison, so the view
    /// must gate on it rather than on "proof exists".
    /// </summary>
    [Fact]
    public void The_sync_button_is_gated_on_the_service_comparison()
    {
        var view = View();
        Assert.Contains("Model.Projection.SyncOffered", view, StringComparison.Ordinal);

        var matching = Projection(skills: [new MockSkillProjectionDto(1, 400, "B2", 50, MockProjectionOutcomeDto.Projected)],
            syncOffered: false, differing: []);
        Assert.False(MockProjectionViewModelMapper.Map(matching, Scores(), 1, NullLogger.Instance).SyncOffered);

        var differing = Projection(skills: [new MockSkillProjectionDto(1, 400, "B2", 50, MockProjectionOutcomeDto.Projected)],
            syncOffered: true, differing: [1]);
        var offered = MockProjectionViewModelMapper.Map(differing, Scores(), 1, NullLogger.Instance);
        Assert.True(offered.SyncOffered);
        Assert.Contains("B2", offered.SyncSummaryText, StringComparison.Ordinal);
    }

    // =============================================================================================
    // AC4: a historical attempt renders its native score and band, never a level.
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> a historical attempt silently projected
    /// against today's table, which reads as a level the learner was never measured against. The mapper
    /// is given the historical shape and must produce no skill rows and no sync.
    /// </summary>
    [Fact]
    public void A_historical_attempt_gets_no_projected_level_and_no_sync()
    {
        var dto = Projection(
            bandCode: "B2",
            skills: [],
            historical: true,
            cannotJudgeReason: null);

        var model = MockProjectionViewModelMapper.Map(dto, Scores(), 1, NullLogger.Instance);

        Assert.True(model.Historical);
        Assert.Empty(model.Skills);
        Assert.Null(model.WeakestProjectedText);
        Assert.False(model.SyncOffered);
        Assert.Contains("before this certificate published a CEFR cut-score table", model.CannotJudgeText, StringComparison.Ordinal);
    }

    // =============================================================================================
    // The unmeasured learner reads "nothing measured yet", not "your level changed".
    // =============================================================================================

    /// <summary>
    /// <b>The input that would pass a wrong implementation:</b> comparing a null stored overall to a
    /// projected level by ignoring the null, so the page reads "your level changed" to a learner who has
    /// never been measured. The correct sentence says nothing is measured yet.
    /// </summary>
    [Fact]
    public void A_learner_with_no_stored_state_is_told_so_rather_than_told_their_level_changed()
    {
        var dto = Projection(
            skills: [new MockSkillProjectionDto(1, 400, "B2", 50, MockProjectionOutcomeDto.Projected)],
            storedOverall: null,
            hasNoStoredMeasurement: true,
            syncOffered: true,
            differing: [1]);

        var model = MockProjectionViewModelMapper.Map(dto, Scores(), 1, NullLogger.Instance);

        Assert.Contains("no measured level yet", model.StoredStateText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------

    private static MockResultProjectionDto Projection(
        MockSkillProjectionDto[] skills,
        int certId = 1,
        string? bandCode = "HIGH",
        string? cannotJudgeReason = null,
        bool historical = false,
        string? storedOverall = null,
        bool hasNoStoredMeasurement = false,
        bool syncOffered = false,
        int[]? differing = null) =>
        new(
            1, certId, 750, bandCode, "High band", 1, skills,
            skills.FirstOrDefault(s => s.AwardedLevelCode is not null)?.AwardedLevelCode,
            skills.FirstOrDefault(s => s.AwardedLevelCode is not null)?.SkillId,
            storedOverall, storedOverall is null ? null : "READ",
            hasNoStoredMeasurement, differing ?? [], syncOffered, historical, cannotJudgeReason, 1);

    private static AttemptSkillScoreDto[] Scores() =>
        [new AttemptSkillScoreDto(1, "READ", "Reading", ModalityDto.Reading, 1, 400, 1)];
}
