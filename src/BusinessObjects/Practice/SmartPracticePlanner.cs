namespace BusinessObjects;

/// <summary>#155: what the picker needs to know about one question in the section being drilled.
/// <paramref name="LastAnsweredAt"/> null means no history row: the question is unseen.</summary>
public readonly record struct PracticeCandidate(
    int QuestionId,
    int? CategoryId,
    DateTimeOffset? LastAnsweredAt,
    DateTimeOffset? CooldownUntil);

/// <summary>
/// #155: the one rule for "which questions a Smart Practice session contains". A pure function of the
/// candidates, the learner's mastery rows and the clock - no query, no AI - so the mix can be read and
/// tested without a database.
///
/// The mix is the ticket's: at most 4 previously-seen questions that are due, and the rest unseen from
/// the same categories. When unseen stock runs out the shortfall is filled from the seen questions
/// answered longest ago, and when fewer than 10 eligible questions exist the session is honestly
/// shorter rather than padded with duplicates.
///
/// Two exclusions do the teaching:
/// - a question answered wrongly is out until its own <c>CooldownUntil</c> passes;
/// - a category whose review is not yet due is out altogether, so "drill it again right now" cannot
///   conjure a green tick out of a wrong answer.
/// </summary>
public static class SmartPracticePlanner
{
    public const int SessionSize = 10;

    /// <summary>How many of a full session may be previously seen. The rest are unseen by construction,
    /// so a full session is at least 6 unseen.</summary>
    public const int MaxSeenPerSession = 4;

    public static IReadOnlyList<int> Plan(
        IReadOnlyCollection<PracticeCandidate> candidates,
        IReadOnlyDictionary<int, MasteryMachine.Snapshot> masteryByCategory,
        DateTimeOffset now,
        int size = SessionSize)
    {
        var eligible = candidates
            .Where(c => c.CooldownUntil is null || c.CooldownUntil <= now)
            .Where(c => c.CategoryId is not { } categoryId
                || !masteryByCategory.TryGetValue(categoryId, out var mastery)
                || MasteryMachine.IsDue(mastery, now))
            .ToList();

        var unseen = eligible
            .Where(c => c.LastAnsweredAt is null)
            .OrderBy(c => c.QuestionId)
            .ToList();

        // "the one answered longest ago" is the ticket's fallback; that is ascending LastAnsweredAt,
        // which also puts the most-forgotten question first.
        var seen = eligible
            .Where(c => c.LastAnsweredAt is not null)
            .OrderBy(c => c.LastAnsweredAt)
            .ThenBy(c => c.QuestionId)
            .ToList();

        var seenTake = Math.Min(seen.Count, Math.Min(MaxSeenPerSession, size));
        var unseenTake = Math.Min(unseen.Count, size - seenTake);

        // Unseen stock ran short: make the session up from the seen side instead of serving eight.
        seenTake = Math.Min(seen.Count, size - unseenTake);

        return unseen.Take(unseenTake)
            .Select(c => c.QuestionId)
            .Concat(seen.Take(seenTake).Select(c => c.QuestionId))
            .ToList();
    }
}
