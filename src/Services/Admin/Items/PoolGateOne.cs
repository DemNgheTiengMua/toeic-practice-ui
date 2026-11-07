using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #194: the one thing gate one cannot answer alone - the cell the item is joining.
/// <para>
/// <see cref="GateOne.Review"/> stays a pure function; this only turns a stored item and its cell
/// into the two arguments that function takes. The cell is counted at the same (TopicId, CefrLevel)
/// <em>excluding this item</em>, because the rule is about the distribution the item makes rather
/// than the one it found. Including it would let an item's own key vouch for its own balance.
/// </para>
/// <para>
/// The cell and "which version is the live one" are <see cref="GateContext"/>'s, so this gate and the
/// ingestion seam cannot come to disagree about either.
/// </para>
/// </summary>
public static class PoolGateOne
{
    /// <summary>Judges the item's current version against the cell it already sits in.</summary>
    public static async Task<GateOneVerdict> ReviewAsync(
        CompanionDbContext db,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Items.AsNoTracking()
            .Where(i => i.ItemId == itemId)
            .Select(i => new
            {
                i.ItemId,
                i.TopicId,
                i.CefrLevel,
                Passage = i.Stimulus == null ? null : i.Stimulus.Passage,
                Transcript = i.Stimulus == null ? null : i.Stimulus.Transcript,
                PromptCopy = i.Stimulus == null ? null : i.Stimulus.PromptCopy,
                // One item's own versions - a handful, so reading them all and asking GateContext which
                // is live costs nothing and keeps the ordering in the one place that states it.
                Versions = i.ItemVersions.ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var version = row is null ? null : GateContext.Newest(row.Versions, v => v.VersionNumber);
        if (row is null || version is null)
        {
            // No item, or an item with nothing authored yet: gate one has nothing to judge, and
            // inventing a refusal for an empty row would refuse every item at creation time.
            return new GateOneVerdict([], []);
        }

        var cell = await GateContext.CellAsync(db, row.TopicId, row.CefrLevel, itemId, cancellationToken);

        return GateOne.Review(
            new GateOneItem(
                version.Stem,
                GateContext.TextOf(row.Passage, row.Transcript, row.PromptCopy),
                version.OptionA,
                version.OptionB,
                version.OptionC,
                version.OptionD,
                version.KeyLabel,
                version.Explanation,
                version.RationaleA,
                version.RationaleB,
                version.RationaleC,
                version.RationaleD),
            cell);
    }

    /// <summary>
    /// Reports a whole cell's key distribution - the half of the balance rule no item can fix, and a
    /// statement about the cell rather than about any item in it.
    /// </summary>
    public static async Task<IReadOnlyList<GateOneNotice>> ReviewCellAsync(
        CompanionDbContext db,
        int topicId,
        CefrLevel level,
        CancellationToken cancellationToken = default) =>
        GateOne.ReviewCell(await GateContext.CellAsync(db, topicId, level, exceptItemId: null, cancellationToken));
}
