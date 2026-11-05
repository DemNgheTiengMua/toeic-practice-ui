using System;
using System.Linq;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #192: the canonical four criteria, as one list both the schema check and the grader read. A pure
/// rule, so no database - the point is that "four" means four named codes, not four rows.
/// </summary>
public sealed class CanonicalRubricTests
{
    [Fact]
    public void Speaking_and_writing_each_have_exactly_four_codes()
    {
        Assert.Equal(["S_FC", "S_PR", "S_LR", "S_GR"], CanonicalRubric.CodesFor("speaking"));
        Assert.Equal(["W_TR", "W_CC", "W_LR", "W_GR"], CanonicalRubric.CodesFor("writing"));
    }

    [Fact]
    public void An_objective_modality_has_no_canonical_rubric()
    {
        // Listening and reading are scored by their key. A "canonical four" for them would be a
        // standard nobody grades against, so there isn't one and the lock refuses.
        Assert.Null(CanonicalRubric.CodesFor("listening"));
        Assert.Null(CanonicalRubric.CodesFor("reading"));
        Assert.False(CanonicalRubric.HasRubric("listening"));
        Assert.True(CanonicalRubric.HasRubric("writing"));
    }

    [Fact]
    public void The_canonical_four_is_accepted()
    {
        Assert.Null(CanonicalRubric.DescribeMismatch("writing", ["W_TR", "W_CC", "W_LR", "W_GR"]));
        // Order is not part of the rule: the codes are a set.
        Assert.Null(CanonicalRubric.DescribeMismatch("writing", ["W_GR", "W_TR", "W_LR", "W_CC"]));
        Assert.Null(CanonicalRubric.DescribeMismatch("SPEAKING", ["s_fc", "s_pr", "s_lr", "s_gr"]));
    }

    [Fact]
    public void Four_rows_under_the_legacy_names_are_refused_and_the_message_names_the_gap()
    {
        // This is the input that passes a count-only guard: exactly four criteria, none of them the
        // canonical four. The seeded TOEIC speaking rubric is in this shape.
        var reason = CanonicalRubric.DescribeMismatch("speaking", ["PRON", "INT", "GRAM", "REL"]);

        Assert.NotNull(reason);
        Assert.Contains("S_FC", reason, StringComparison.Ordinal);
        Assert.Contains("missing", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRON", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fifth_code_is_refused_and_named()
    {
        var reason = CanonicalRubric.DescribeMismatch(
            "writing", ["W_TR", "W_CC", "W_LR", "W_GR", "W_XT"]);

        Assert.NotNull(reason);
        Assert.Contains("W_XT", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_code_does_not_count_as_four()
    {
        // Four rows again, but one code twice: the set is three, so the rubric is incomplete.
        var reason = CanonicalRubric.DescribeMismatch(
            "writing", ["W_TR", "W_CC", "W_LR", "W_LR"]);

        Assert.NotNull(reason);
        Assert.Contains("W_GR", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_band_set_is_seven_consecutive_integers_starting_at_zero()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], CanonicalRubric.Bands.Select(b => (int)b));
        Assert.Equal(6, CanonicalRubric.HighestBand);
    }
}
