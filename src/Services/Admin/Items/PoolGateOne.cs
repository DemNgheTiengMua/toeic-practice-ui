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
/// Each sibling contributes its latest version's key: an item keyed B in version 1 and C in version 2
/// is one key, and it is C.
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
                Version = i.ItemVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row?.Version is null)
        {
            // No item, or an item with nothing authored yet: gate one has nothing to judge, and
            // inventing a refusal for an empty row would refuse every item at creation time.
            return new GateOneVerdict([], []);
        }

        var cell = await CellAsync(db, row.TopicId, row.CefrLevel, exceptItemId: itemId, cancellationToken);

        return GateOne.Review(
            new GateOneItem(
                row.Version.Stem,
                row.Passage ?? row.Transcript ?? row.PromptCopy,
                row.Version.OptionA,
                row.Version.OptionB,
                row.Version.OptionC,
                row.Version.OptionD,
                row.Version.KeyLabel,
                row.Version.Explanation,
                row.Version.RationaleA,
                row.Version.RationaleB,
                row.Version.RationaleC,
                row.Version.RationaleD),
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
        GateOne.ReviewCell(await CellAsync(db, topicId, level, exceptItemId: null, cancellationToken));

    private static async Task<GateOneCell> CellAsync(
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

        var latestKeys = versions
            .GroupBy(v => v.ItemId)
            .Select(siblings => siblings.OrderByDescending(v => v.VersionNumber).First().KeyLabel);

        return GateOneCell.FromLabels(latestKeys);
    }
}
