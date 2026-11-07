using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// D17: the three facts every authoring gate reads from the database, answered in one place.
/// <para>
/// A gate is a pure function over an item plus its surroundings. The item comes from the caller; the
/// surroundings - the cell it joins, the text a learner will read, and which version of the item is
/// the live one - do not. Each door used to assemble those by hand, which is how one question came to
/// have two answers.
/// </para>
/// <para>
/// <c>internal</c> like <c>PoolGateOne.CellAsync</c> was: the seam is inside <c>Services</c>, and no
/// window or endpoint hands a gate its context.
/// </para>
/// </summary>
internal static class GateContext
{
    /// <summary>
    /// <b>Which cell the item joins.</b> The keys already stored at (topic, level), one per item,
    /// excluding <paramref name="exceptItemId"/>. Key balance is a property of the cell and not of an
    /// item, so it is counted from the rows rather than from anything the caller holds.
    /// <para>
    /// Each sibling contributes its newest version's key: an item keyed B in version 1 and C in
    /// version 2 is one key, and it is C.
    /// </para>
    /// </summary>
    public static async Task<GateOneCell> CellAsync(
        CompanionDbContext db,
        int topicId,
        CefrLevel level,
        int? exceptItemId,
        CancellationToken cancellationToken)
    {
        if (level.IsReportableFloor)
        {
            throw new ArgumentException(
                $"'{level.Code}' is the reportable floor; content is authored at A1-C2 only.", nameof(level));
        }

        // Two columns and the newest key per item: small enough to finish in memory, and a correlated
        // first-per-group is the one shape EF translates differently on every provider.
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => v.Item.TopicId == topicId
                        && v.Item.CefrLevel == level
                        && (exceptItemId == null || v.ItemId != exceptItemId))
            .Select(v => new { v.ItemId, v.VersionNumber, v.KeyLabel })
            .ToListAsync(cancellationToken);

        return GateOneCell.FromLabels(
            NewestPerItem(versions, v => v.ItemId, v => v.VersionNumber).Select(v => v.KeyLabel));
    }

    /// <summary>
    /// The stored stimulus a child hangs off, or null when there is no such row - which is what makes
    /// this also the existence check on the create door. Read once and handed to the gate, because the
    /// text the lemma rule compares against is the same text the caller has just proved exists.
    /// </summary>
    public static async Task<Stimulus?> FindStimulusAsync(
        CompanionDbContext db,
        int? stimulusId,
        CancellationToken cancellationToken) =>
        stimulusId is int id
            ? await db.Stimuli.AsNoTracking().FirstOrDefaultAsync(s => s.StimulusId == id, cancellationToken)
            : null;

    /// <summary>
    /// <b>What the learner will read.</b> The passage, transcript or prompt copy of one stimulus, in
    /// that order; null when it stores none of them (an image-only or audio-only stimulus).
    /// </summary>
    public static string? TextOf(string? passage, string? transcript, string? promptCopy) =>
        passage ?? transcript ?? promptCopy;

    /// <inheritdoc cref="TextOf(string?,string?,string?)"/>
    public static string? TextOf(Stimulus? stimulus) =>
        TextOf(stimulus?.Passage, stimulus?.Transcript, stimulus?.PromptCopy);

    /// <summary>
    /// <b>Which version is the live one.</b> Newest means the highest <c>VersionNumber</c>: the authored
    /// ordering, where the next version is the current one plus one.
    /// <para>
    /// <b>Not the highest <c>ItemVersionId</c>.</b> The two orders agreed on every row measured across
    /// seventeen catalogs on 2026-11-07, but nothing constrains them to - <c>ItemVersionId</c> is an
    /// identity, so a version inserted out of order (a seed, a hand fix, a replayed import) makes them
    /// disagree, and on such a row the two readers would pick different keys and different spans. This
    /// is the one place the ordering is stated; every reader asks here rather than restating it.
    /// <c>VersionNumber</c> wins because it is what the author wrote, and because it is the same order
    /// <c>trg_ReviewSampleDecisions_NoSelfApproval</c> reads when it asks who last wrote an item.
    /// </para>
    /// </summary>
    public static T? Newest<T>(IEnumerable<T> versions, Func<T, int> versionNumber) =>
        versions.MaxBy(versionNumber);

    /// <summary>
    /// The newest version of each item, keyed by <paramref name="itemId"/> - the same ordering
    /// <see cref="Newest{T}"/> states, for a query that reads a whole cell or a whole bundle at once.
    /// </summary>
    public static IEnumerable<T> NewestPerItem<T>(
        IEnumerable<T> versions,
        Func<T, int> itemId,
        Func<T, int> versionNumber) =>
        versions.GroupBy(itemId).Select(siblings => Newest(siblings, versionNumber)!);
}
