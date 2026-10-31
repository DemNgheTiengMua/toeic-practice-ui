using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #147: the five invariants a certificate must satisfy before it is saved. These are pure rules —
/// no database — so each one states the rule and the message the admin sees.
/// </summary>
public sealed class CertificateConfigurationValidatorTests
{
    [Fact]
    public void A_continuous_set_of_bands_is_accepted()
    {
        CertificateConfigurationValidator.Validate(Request(0, 50, 1,
            [Band("FAIL", 0, 24, 1), Band("PASS", 25, 50, 2)]));
    }

    [Fact]
    public void A_gap_between_bands_is_refused_and_the_message_names_the_gap()
    {
        // 25-29 is covered by nothing. The message has to say so, or the admin cannot fix it.
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 1,
                [Band("FAIL", 0, 10, 1), Band("PASS", 20, 50, 2)])));

        Assert.Contains("11", error.Message, StringComparison.Ordinal);
        Assert.Contains("19", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gap_at_the_top_of_the_range_is_refused()
    {
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 1,
                [Band("FAIL", 0, 24, 1), Band("PASS", 25, 40, 2)])));

        Assert.Contains("50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlapping_bands_are_refused()
    {
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 1,
                [Band("FAIL", 0, 30, 1), Band("PASS", 25, 50, 2)])));

        Assert.Equal(ScoreConversionException.OverlappingBands, error.Code);
    }

    [Fact]
    public void A_zero_score_step_is_refused()
    {
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 0,
                [Band("FAIL", 0, 24, 1), Band("PASS", 25, 50, 2)])));

        Assert.Contains("ScoreStep", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_maximum_at_or_below_the_minimum_is_refused()
    {
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(50, 50, 1,
                [Band("FAIL", 0, 24, 1), Band("PASS", 25, 50, 2)])));

        Assert.Equal(ScoreConversionException.InvalidBandRange, error.Code);
    }

    [Fact]
    public void A_criterion_without_a_rubric_description_is_refused_and_named()
    {
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 1,
                [Band("FAIL", 0, 24, 1), Band("PASS", 25, 50, 2)],
                criteria: [Criterion("TASK", rubric: null)])));

        Assert.Contains("TASK", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_certificate_with_no_bands_is_refused()
    {
        // The exact configuration that used to be saved and then failed every placement submission.
        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(Request(0, 50, 1, [])));

        Assert.Contains("band", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SaveCertificateWizardRequest Request(
        int min, int max, int step,
        IReadOnlyList<SaveScoreBandWizardDto> bands,
        IReadOnlyList<SaveGradingCriterionWizardDto>? criteria = null) =>
        new(
            CertId: null, Code: "T", Name: "T", Description: null,
            ScoreAggregationRule: "sum", PresetCode: "custom", IsActive: true,
            Skills: [], MinScaledScore: min, MaxScaledScore: max, ScoreStep: step,
            Bands: bands, Criteria: criteria);

    private static SaveScoreBandWizardDto Band(string code, int min, int max, int order) =>
        new(null, code, code, min, max, order);

    private static SaveGradingCriterionWizardDto Criterion(string code, string? rubric) =>
        new(null, "EXPR", code, code, 5m, 1m, 1, rubric);
}
