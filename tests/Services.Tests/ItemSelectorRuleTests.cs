using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #235 / PLC-58..59: the one selector's two pure halves — the cooldowns and the ordering — asserted
/// without a database, so a wrong constant or a swallowed tie-break cannot hide behind plumbing.
/// <para>
/// Every guard below states the input that would pass a weaker version while being wrong, and the test
/// that catches it.
/// </para>
/// </summary>
public sealed class ItemSelectorRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private static PoolCandidate Candidate(
        int id,
        string itemType = "sentence_cloze",
        int timesSeen = 0,
        DateTimeOffset? lastSeenAt = null,
        DateTimeOffset? lastWrongAt = null,
        DateTimeOffset? lastCorrectAt = null,
        int topicId = 1) =>
        new(id, id * 10, topicId, itemType, timesSeen, lastSeenAt, lastWrongAt, lastCorrectAt);

    // ---------------------------------------------------------------------------------------------
    // The three cooldowns (PLC-59 / D10).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The three lengths are <b>distinct</b>. The input that would pass a weaker test while being wrong
    /// is the one where two of them are equal: a 72-hour correct-discrete and a 72-hour comprehension
    /// would satisfy "a correct answer cools", and only an inequality on all three pairs catches it.
    /// </summary>
    [Fact]
    public void The_three_cooldown_lengths_are_pairwise_distinct()
    {
        var wrong = ItemCooldown.WrongAnswer;
        var discrete = ItemCooldown.CorrectDiscrete;
        var comprehension = ItemCooldown.CorrectComprehension;

        Assert.Equal(TimeSpan.FromHours(24), wrong);
        Assert.Equal(TimeSpan.FromHours(72), discrete);
        Assert.Equal(TimeSpan.FromDays(14), comprehension);

        Assert.NotEqual(wrong, discrete);
        Assert.NotEqual(wrong, comprehension);
        Assert.NotEqual(discrete, comprehension);
    }

    /// <summary>
    /// A wrong answer rests 24 hours and a correct comprehension answer rests 14 days. The input that
    /// would pass while being wrong: applying one length to both, which reads as correct while the item
    /// the learner just got wrong comes back tomorrow and the passage they just read comes back in three
    /// days.
    /// </summary>
    [Fact]
    public void A_wrong_answer_and_a_correct_comprehension_answer_cool_for_different_lengths()
    {
        var wrong = Candidate(1, "passage_comprehension", lastWrongAt: Now);
        var correct = Candidate(2, "passage_comprehension", lastCorrectAt: Now);

        Assert.Equal(Now + TimeSpan.FromHours(24), ItemCooldown.CooldownUntil(wrong));
        Assert.Equal(Now + TimeSpan.FromDays(14), ItemCooldown.CooldownUntil(correct));
    }

    /// <summary>
    /// The item's <b>shape</b> decides the correct-answer cooldown: 72 hours at a discrete item, 14 days
    /// at a comprehension one. Passing while wrong: keying the cooldown on the modality rather than the
    /// shape, which makes every reading item rest 14 days.
    /// </summary>
    [Fact]
    public void A_correct_discrete_item_rests_72_hours_and_a_comprehension_item_rests_14_days()
    {
        var discrete = Candidate(1, "sentence_cloze", lastCorrectAt: Now);
        var comprehension = Candidate(2, "passage_comprehension", lastCorrectAt: Now);

        Assert.Equal(Now + TimeSpan.FromHours(72), ItemCooldown.CooldownUntil(discrete));
        Assert.Equal(Now + TimeSpan.FromDays(14), ItemCooldown.CooldownUntil(comprehension));
        Assert.True(ItemCooldown.CooldownUntil(comprehension) > ItemCooldown.CooldownUntil(discrete));
    }

    /// <summary>
    /// The <b>last</b> answer decides, not "any wrong answer ever". Passing while wrong: keeping the
    /// wrong-answer cooldown after a later correct answer, which locks an item the learner has since
    /// answered properly.
    /// </summary>
    [Fact]
    public void A_later_correct_answer_replaces_the_wrong_answers_cooldown()
    {
        var recovered = Candidate(
            1, "sentence_cloze",
            lastWrongAt: Now.AddHours(-30),
            lastCorrectAt: Now.AddHours(-1));

        // 72 hours from the correct answer, not 24 from the wrong one.
        Assert.Equal(Now.AddHours(71), ItemCooldown.CooldownUntil(recovered));
    }

    /// <summary>An unseen item is servable immediately: no answer, no cooldown.</summary>
    [Fact]
    public void An_unseen_item_has_no_cooldown()
    {
        Assert.Null(ItemCooldown.CooldownUntil(Candidate(1)));
    }

    // ---------------------------------------------------------------------------------------------
    // The ordering (PLC-58).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Least seen, then least recently seen, then identifier</b>, asserted against a fixture where
    /// the three keys disagree pairwise. The input that would pass "run it twice" while being wrong is
    /// any stable order that is not this one — so the assertion is the exact sequence, never a repeat of
    /// the same call.
    /// <para>
    /// ids 4, 3, 2, 1 are written so that: 4 is unseen (least seen); 3 and 2 are both seen once, and 3
    /// is older (least recent); 1 is seen twice (most seen) and is the lowest id, so an ordering by id
    /// alone would put it first.
    /// </para>
    /// </summary>
    [Fact]
    public void The_order_is_least_seen_then_least_recent_then_identifier()
    {
        var candidates = new[]
        {
            Candidate(1, timesSeen: 2, lastSeenAt: Now.AddDays(-1)),
            Candidate(2, timesSeen: 1, lastSeenAt: Now.AddHours(-2)),
            Candidate(3, timesSeen: 1, lastSeenAt: Now.AddDays(-9)),
            Candidate(4, timesSeen: 0),
        };

        var ranked = PoolRanking.Rank(candidates).Select(c => c.ItemId).ToList();

        Assert.Equal([4, 3, 2, 1], ranked);
    }

    /// <summary>
    /// Two items with identical history fall back to the identifier, so the draw is reproducible. The
    /// input that would pass while being wrong is a tie broken by insertion order, which changes with
    /// whatever order the query happened to return.
    /// </summary>
    [Fact]
    public void Identical_history_is_broken_by_item_identifier()
    {
        var a = Candidate(7, timesSeen: 1, lastSeenAt: Now.AddHours(-3));
        var b = Candidate(5, timesSeen: 1, lastSeenAt: Now.AddHours(-3));

        Assert.Equal([5, 7], PoolRanking.Rank([a, b]).Select(c => c.ItemId));
        Assert.Equal([5, 7], PoolRanking.Rank([b, a]).Select(c => c.ItemId));
    }

    // ---------------------------------------------------------------------------------------------
    // Exclusion and honesty about size.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An item inside its cooldown is not served. Passing while wrong: excluding only the 24-hour wrong
    /// cooldown, which serves a passage the learner read nine days ago as though it were new.
    /// </summary>
    [Fact]
    public void An_item_inside_a_correct_comprehension_cooldown_is_not_selected()
    {
        var cooling = Candidate(1, "passage_comprehension", timesSeen: 1, lastSeenAt: Now.AddDays(-7), lastCorrectAt: Now.AddDays(-7));
        var servable = Candidate(2, "sentence_cloze", timesSeen: 1, lastSeenAt: Now.AddDays(-7), lastCorrectAt: Now.AddDays(-7));

        var selection = PoolRanking.Select([cooling, servable], Now, size: 10);

        Assert.Equal([2], selection.Items.Select(i => i.ItemId));
    }

    /// <summary>
    /// A cell holding fewer servable items than were asked for returns <b>what exists</b> and says it is
    /// short. Passing while wrong: padding with a repeat to reach the requested size, which is what
    /// "never padded with repeats" forbids.
    /// </summary>
    [Fact]
    public void Asking_for_more_than_a_cell_holds_returns_what_exists_and_is_marked_short()
    {
        var selection = PoolRanking.Select([Candidate(1), Candidate(2)], Now, size: 10);

        Assert.Equal(2, selection.Items.Count);
        Assert.Equal(2, selection.Items.Select(i => i.ItemId).Distinct().Count());
        Assert.True(selection.IsShort);

        var exact = PoolRanking.Select([Candidate(1), Candidate(2)], Now, size: 2);
        Assert.False(exact.IsShort);
    }

    /// <summary>
    /// The soonest a cooling cell reopens, so a refusal can name a time of day instead of "nothing
    /// available". Passing while wrong: returning the <em>last</em> cooldown to expire.
    /// </summary>
    [Fact]
    public void The_soonest_servable_moment_is_the_earliest_cooldown_not_the_latest()
    {
        var soon = Candidate(1, lastWrongAt: Now.AddHours(-20));      // +4h
        var late = Candidate(2, "passage_comprehension", lastCorrectAt: Now); // +14d

        Assert.Equal(Now.AddHours(4), PoolRanking.SoonestServableAt([soon, late], Now));
    }
}
