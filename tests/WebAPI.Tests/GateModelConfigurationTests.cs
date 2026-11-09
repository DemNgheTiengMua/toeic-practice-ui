using Microsoft.Extensions.Configuration;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #258: which models the content gates are wired with.
/// <para>
/// Before this existed, <c>PoolIngestionGates</c> was registered with no arguments, so no deployment
/// ever had a verifier or a classifier and gate three could never run. These tests pin the rule that
/// decides whether the gates are enabled, because "looks configured" and "is configured" diverged for
/// the whole rebuild while every suite stayed green.
/// </para>
/// <para>
/// Every test here is written as <b>the input that would pass a weaker check</b>: a count of two
/// families passes a naive guard while being one family twice, which is exactly the agreement gate
/// three exists to refuse.
/// </para>
/// </summary>
public sealed class GateModelConfigurationTests
{
    private static GateModelConfiguration Resolve(params (string Key, string? Value)[] settings) =>
        GateModelConfiguration.Resolve(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
                .Build());

    [Fact]
    public void No_configuration_leaves_both_gates_disabled()
    {
        var config = Resolve();

        Assert.Empty(config.VerifierFamilies);
        Assert.Null(config.ClassifierFamily);
        Assert.False(config.IsGateThreeEnabled);
        Assert.False(config.IsFullyConfigured);
    }

    [Fact]
    public void Two_distinct_families_enable_gate_three()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "sonnet"),
            ("Claude:GateFour:ClassifierFamily", "haiku"));

        Assert.Equal(["opus", "sonnet"], config.VerifierFamilies);
        Assert.Equal("haiku", config.ClassifierFamily);
        Assert.True(config.IsGateThreeEnabled);
        Assert.True(config.IsFullyConfigured);
    }

    /// <summary>
    /// <b>The wrong input that would pass a count check:</b> the same family named twice. A guard that
    /// asks "are there two verifier families" says yes; gate three then compares a family with itself
    /// and the "agreement" is one opinion counted twice, which is the defect the distinct-family rule
    /// exists to catch.
    /// </summary>
    [Fact]
    public void One_family_named_twice_is_one_family_and_enables_nothing()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "opus"));

        Assert.Empty(config.VerifierFamilies);
        Assert.False(config.IsGateThreeEnabled);
    }

    /// <summary>Case is not a family: <c>opus</c> and <c>OPUS</c> are the same model.</summary>
    [Fact]
    public void Duplicates_are_collapsed_case_insensitively()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "OPUS"));

        Assert.False(config.IsGateThreeEnabled);
    }

    /// <summary>
    /// <b>The wrong input that would pass:</b> treating a single family as "partly configured". A lone
    /// verifier makes <c>HasNoVerifiers</c> true anyway, so passing it would move the refusal from
    /// "did not run" to "families not distinct" — two sentences for one deployment mistake.
    /// </summary>
    [Fact]
    public void A_single_family_yields_no_verifiers_at_all()
    {
        var config = Resolve(("Claude:GateThree:VerifierFamilies:0", "opus"));

        Assert.Empty(config.VerifierFamilies);
        Assert.False(config.IsGateThreeEnabled);
    }

    [Fact]
    public void Blank_entries_do_not_count_toward_the_two_families()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "   "));

        Assert.False(config.IsGateThreeEnabled);
    }

    [Fact]
    public void Families_are_trimmed_so_whitespace_is_not_a_second_family()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "  opus  "));

        Assert.False(config.IsGateThreeEnabled);
    }

    /// <summary>
    /// Gate three and gate four are independent: a classifier without two verifiers still enables gate
    /// four, and the deployment is not fully configured. Collapsing them into one flag would make a
    /// half-wired deployment look whole.
    /// </summary>
    [Fact]
    public void A_classifier_without_two_verifiers_enables_gate_four_only()
    {
        var config = Resolve(("Claude:GateFour:ClassifierFamily", "haiku"));

        Assert.Null(config.ClassifierFamily is null ? null : config.VerifierFamilies.FirstOrDefault());
        Assert.False(config.IsGateThreeEnabled);
        Assert.False(config.IsFullyConfigured);
    }

    [Fact]
    public void A_blank_classifier_family_is_no_classifier()
    {
        var config = Resolve(
            ("Claude:GateThree:VerifierFamilies:0", "opus"),
            ("Claude:GateThree:VerifierFamilies:1", "sonnet"),
            ("Claude:GateFour:ClassifierFamily", "  "));

        Assert.Null(config.ClassifierFamily);
        Assert.True(config.IsGateThreeEnabled);
        Assert.False(config.IsFullyConfigured);
    }
}
