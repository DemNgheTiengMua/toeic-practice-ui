using System;
using System.Collections.Generic;

namespace Services;

/// <summary>
/// #206 / S-03: one cell of one batch, as the audit queue shows it.
/// <para>
/// <see cref="Sampled"/> and <see cref="Unsampled"/> are both on the row because that is the fact the
/// screen has to state: approving promotes both counts, not <see cref="Sampled"/> alone. A queue that
/// showed only the sample would make "publish 10% of the pool" look like the intended outcome.
/// </para>
/// </summary>
public sealed record AuditStratumRow(
    int StratumId,
    int BatchId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    int ItemCount,
    int SampleSize,
    int Sampled,
    int Unsampled,
    string Status,
    string? QuarantineReason,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<AuditSampleRow> Items);

/// <summary>
/// #206: one drawn item. <see cref="KeyLabel"/> is deliberately not here: this row is what the queue
/// renders <em>before</em> the key is read, and a projection that carried the key would reveal it to
/// anyone who could read a list - which is every reviewer, at every moment.
/// </summary>
public sealed record AuditSampleRow(
    int SampleItemId,
    int ItemId,
    string Stem,
    string OptionA,
    string OptionB,
    string OptionC,
    string OptionD,
    string? SolvedLabel,
    DateTimeOffset? SolvedAt,
    DateTimeOffset? KeyRevealedAt,
    bool KeyRevealed,
    string? Verdict)
{
    public bool IsSolved => SolvedLabel is not null;

    public bool IsJudged => Verdict is not null;
}

/// <summary>
/// #206: the authored key, and the one call that returns it. Separate from <see cref="AuditSampleRow"/>
/// so the reveal is a distinct read with its own precondition rather than a property that happens to be
/// populated.
/// </summary>
public sealed record AuditKeyReveal(
    int SampleItemId,
    int ItemId,
    string KeyLabel,
    string? Explanation,
    string? SolvedLabel,
    DateTimeOffset RevealedAt);

/// <summary>#206: the queue for one batch, with the one number the promotion sentence is made of.</summary>
public sealed record AuditQueue(
    int BatchId,
    string SourceName,
    string Status,
    IReadOnlyList<AuditStratumRow> Strata)
{
    public int SampledTotal
    {
        get
        {
            var total = 0;
            foreach (var stratum in Strata)
            {
                total += stratum.Sampled;
            }

            return total;
        }
    }

    /// <summary>
    /// The 90% that was never individually reviewed. This is what "promotion scope" is about, and it is
    /// computed rather than assumed so the screen cannot state a number the database disagrees with.
    /// </summary>
    public int UnsampledTotal
    {
        get
        {
            var total = 0;
            foreach (var stratum in Strata)
            {
                total += stratum.Unsampled;
            }

            return total;
        }
    }
}

/// <summary>
/// #206: what an approval or a quarantine did. <see cref="Promoted"/> is the whole stratum - the sample
/// plus the unsampled remainder - and it is reported so a caller can see that a 2-item sample moved 20
/// items, rather than having to trust the sentence on the screen.
/// </summary>
public sealed record AuditDecisionResult(
    int StratumId,
    string Status,
    string? QuarantineReason,
    int SampledPromoted,
    int UnsampledPromoted,
    int Quarantined);

/// <summary>#206: one verdict a reviewer records against a drawn item.</summary>
public enum AuditVerdict
{
    /// <summary>The reviewer's solve matched the authored key.</summary>
    Correct,

    /// <summary>The authored key is wrong. One of these quarantines the whole stratum (story 31).</summary>
    KeyError,

    /// <summary>Level drift, an unnatural distractor, a broken explanation: real, but not a key error.</summary>
    NonKeyDefect,
}
