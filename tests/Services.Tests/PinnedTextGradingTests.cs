using System.Collections.Generic;
using BusinessObjects;
using BusinessObjects.Entities;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269: a pinned version's text answer is judged by the two things the version actually stores - its
/// <c>AcceptedAnswersJson</c> and its keyed option texts. Before this, <c>IsKeyLabel</c> alone judged the
/// answer, so an <c>inline_completion</c> on the pool could never be correct: it has no key to match a
/// label against.
/// <para>
/// These are pure functions over an <see cref="ItemVersion"/>, so no database is needed and the three ways
/// the rule can be wrong are each pinned separately.
/// </para>
/// </summary>
public sealed class PinnedTextGradingTests
{
    private static ItemVersion Blank(string? accepted = "[\"photosynthesis\"]", string? keywords = null) => new()
    {
        ItemId = 1,
        VersionNumber = 1,
        Stem = "Complete the sentence.",
        AcceptedAnswersJson = accepted,
        RequiredKeywords = keywords,
    };

    [Fact]
    public void An_accepted_answer_is_right_whatever_its_case_or_spacing()
    {
        var version = Blank();

        Assert.True(AttemptEngine.EvaluatePinnedText(version, "photosynthesis"));
        Assert.True(AttemptEngine.EvaluatePinnedText(version, "Photosynthesis"));
        Assert.True(AttemptEngine.EvaluatePinnedText(version, "  PHOTOSYNTHESIS  "));
    }

    [Fact]
    public void Anything_else_is_wrong()
    {
        var version = Blank();

        Assert.False(AttemptEngine.EvaluatePinnedText(version, "respiration"));
        Assert.False(AttemptEngine.EvaluatePinnedText(version, ""));
        Assert.False(AttemptEngine.EvaluatePinnedText(version, null));
    }

    /// <summary>
    /// The keyed option texts count too. A version that carries a key but also expects typed text is not a
    /// shape the schema allows today, but the reader must not silently ignore the key when a text answer
    /// arrives - that is how the legacy path scored it.
    /// </summary>
    [Fact]
    public void A_keyed_option_text_is_also_an_accepted_answer()
    {
        var version = new ItemVersion
        {
            ItemId = 2,
            VersionNumber = 1,
            Stem = "Complete the sentence.",
            OptionA = "at",
            OptionB = "on",
            OptionC = "to",
            OptionD = "by",
            KeyLabel = "A",
        };

        Assert.True(AttemptEngine.EvaluatePinnedText(version, "at"));
        Assert.False(AttemptEngine.EvaluatePinnedText(version, "on"));
    }

    /// <summary>
    /// The declared ceiling still applies, and a digit is still refused when the author forbade one - the
    /// two rules <c>EvaluateTextCompletion</c> applies to a legacy question. Dropping them here would have
    /// made the pool a laxer gate than the paper it replaced.
    /// </summary>
    [Fact]
    public void The_word_ceiling_and_the_digit_rule_still_apply()
    {
        var tooLong = new ItemVersion
        {
            ItemId = 3, VersionNumber = 1, Stem = "s",
            AcceptedAnswersJson = "[\"the process by which plants make food\"]",
            MaxWords = 2,
        };
        Assert.False(AttemptEngine.EvaluatePinnedText(tooLong, "the process by which plants make food"));

        var noDigits = new ItemVersion
        {
            ItemId = 4, VersionNumber = 1, Stem = "s",
            AcceptedAnswersJson = "[\"carbon dioxide\"]",
        };
        Assert.False(AttemptEngine.EvaluatePinnedText(noDigits, "carbon dioxide 2"));
    }

    /// <summary>A declared required keyword must be present, by stem, exactly as the legacy rule asks.</summary>
    [Fact]
    public void A_required_keyword_must_appear()
    {
        var version = Blank(accepted: "[\"the team is discussing the proposal\"]", keywords: "discuss,proposal");

        Assert.True(AttemptEngine.EvaluatePinnedText(version, "The team is discussing the proposal."));
        Assert.False(AttemptEngine.EvaluatePinnedText(version, "The team is talking about it."));
    }
}
