namespace BusinessObjects;

/// <summary>
/// #235 / PLC-58: one pool item, with everything the one selector needs to rank and cool it. The
/// statistics are this learner's own answers at this certificate, read from <c>PracticeAnswers</c> —
/// never from <c>UserQuestionHistories</c>, which is keyed by the legacy <c>QuestionId</c> and would
/// match the wrong rows.
/// </summary>
public readonly record struct PoolCandidate(
    int ItemId,
    int ItemVersionId,
    int TopicId,
    string ItemType,
    int TimesSeen,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastWrongAt,
    DateTimeOffset? LastCorrectAt);

/// <summary>#235: what one draw produced. <see cref="IsShort"/> means the cell held fewer servable
/// items than were asked for — the draw returns what exists and is never padded with repeats.</summary>
public sealed record PoolSelection(IReadOnlyList<PoolCandidate> Items, bool IsShort)
{
    public static readonly PoolSelection Empty = new([], IsShort: false);

    public bool IsEmpty => Items.Count == 0;
}

/// <summary>
/// #235 / PLC-59: the three item cooldowns. They differ by <b>answer</b> and by <b>item shape</b>:
/// a wrong answer always rests 24 hours, while a correct answer rests 72 hours at a discrete item and
/// 14 days at a comprehension one — repeating an identical passage measures recall of the passage, not
/// comprehension.
/// <para>
/// Only two of the three are new. The 24-hour wrong-answer cooldown is
/// <see cref="MasterySchedule.WrongAnswerCooldown"/> and is referenced, not restated, so the writer and
/// the selector cannot drift.
/// </para>
/// <para>
/// A productive item (speaking, writing) has no passage to re-read, so it takes the shorter correct
/// cooldown: it is not a comprehension stimulus.
/// </para>
/// </summary>
public static class ItemCooldown
{
    /// <summary>PLC-59: "a wrong objective answer sets a 24-hour cooldown on that item".</summary>
    public static readonly TimeSpan WrongAnswer = MasterySchedule.WrongAnswerCooldown;

    /// <summary>PLC-59 / D0.10: "item cooldown, correct discrete grammar — 72 hours".</summary>
    public static readonly TimeSpan CorrectDiscrete = TimeSpan.FromHours(72);

    /// <summary>PLC-59 / D0.10: "item cooldown, correct comprehension — 14 days".</summary>
    public static readonly TimeSpan CorrectComprehension = TimeSpan.FromDays(14);

    /// <summary>
    /// D0.4's shapes: a reading passage, and every listening type, are answered against a stimulus the
    /// learner read or heard. The other types have no stimulus to memorise.
    /// </summary>
    public static bool IsComprehensionStimulus(string itemType) => itemType is
        "passage_comprehension" or "short_exchange" or "extended_dialogue" or "monologue_talk";

    public static TimeSpan CorrectCooldownFor(string itemType) =>
        IsComprehensionStimulus(itemType) ? CorrectComprehension : CorrectDiscrete;

    /// <summary>
    /// When this item becomes servable again, or null when it is servable now. The <b>last answer</b>
    /// decides: a wrong answer always rests 24 hours, a correct one rests by the item's shape.
    /// </summary>
    public static DateTimeOffset? CooldownUntil(PoolCandidate item)
    {
        var (last, wasWrong) = (item.LastWrongAt, item.LastCorrectAt) switch
        {
            ({ } wrong, { } correct) when wrong >= correct => ((DateTimeOffset?)wrong, true),
            ({ } _, { } correct) => (correct, false),
            ({ } wrong, null) => (wrong, true),
            (null, { } correct) => (correct, false),
            _ => (null, false),
        };

        if (last is not { } at)
        {
            return null;
        }

        return at + (wasWrong ? WrongAnswer : CorrectCooldownFor(item.ItemType));
    }
}

/// <summary>
/// #235 / PLC-58: the ordering half of the one selector, as a pure function so a fixture whose three
/// keys disagree pairwise can be asserted without a database.
/// <para>
/// <b>Least seen, then least recently seen, then identifier.</b> Unseen items come first by
/// construction, and a repeated draw is a reproducible choice rather than whatever the storage engine
/// returns.
/// </para>
/// </summary>
public static class PoolRanking
{
    public static IReadOnlyList<PoolCandidate> Rank(IEnumerable<PoolCandidate> candidates) =>
        candidates
            .OrderBy(c => c.TimesSeen)
            .ThenBy(c => c.LastSeenAt ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.ItemId)
            .ToList();

    /// <summary>
    /// The servable items of a cell at <paramref name="now"/>: cooling items excluded, then ranked and
    /// cut to <paramref name="size"/>. <c>IsShort</c> says the cell held fewer than were asked for.
    /// </summary>
    public static PoolSelection Select(
        IEnumerable<PoolCandidate> candidates, DateTimeOffset now, int size)
    {
        var eligible = Rank(candidates.Where(c => IsServable(c, now)));
        var take = eligible.Count > size ? eligible.Take(size).ToList() : eligible.ToList();
        return new PoolSelection(take, IsShort: take.Count < size);
    }

    public static bool IsServable(PoolCandidate item, DateTimeOffset now) =>
        ItemCooldown.CooldownUntil(item) is not { } until || until <= now;

    /// <summary>The soonest moment anything in the cell becomes servable, or null when it already is
    /// or the cell is empty. A refusal says "come back at 09:00" rather than "nothing available".</summary>
    public static DateTimeOffset? SoonestServableAt(IEnumerable<PoolCandidate> candidates, DateTimeOffset now) =>
        candidates
            .Select(ItemCooldown.CooldownUntil)
            .OfType<DateTimeOffset>()
            .Where(until => until > now)
            .DefaultIfEmpty()
            .Min() is { } soonest && soonest != default
            ? soonest
            : null;
}
