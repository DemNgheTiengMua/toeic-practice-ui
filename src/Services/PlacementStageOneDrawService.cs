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
/// #214 / D7, PLC-22: stage one's blueprint dispatch - one fixed per-band distribution, drawn for every
/// objective skill the certificate examines.
/// <para>
/// <b>The draw is over the pool, and every child is its own candidate.</b> An item is addressed by
/// (skill, level, topic) and a stimulus is shared by three or four children, so the selector chooses
/// items, never bundles. A band that asks for one item therefore takes one of a bundle's children
/// rather than the whole bundle or nothing - which is what makes a one-item stage-one draw possible at
/// all. <see cref="PlacementStageOneBlueprint.Select"/> owns that rule; this class only supplies the
/// candidates.
/// </para>
/// <para>
/// <b>Selection reads the same three columns as a drill.</b> Lifecycle <c>active</c> (the six states are
/// the database's), the paper's own content tier (INV-6), and <c>PlacementEligible</c> - the one flag
/// that says placement may draw this item at all (CNT-24). A quarantined or practice-only item is
/// never served into a placement, and this is the single query that decides it.
/// </para>
/// <para>
/// <b>A deficit is reported, not repaired.</b> A band with too little stock comes back short and named;
/// substituting an adjacent band would change the denominator the 70% rule is computed over (PLC-26).
/// What to do about a deficit - refuse, or run a degraded form - belongs to the caller (#215).
/// </para>
/// </summary>
public sealed class PlacementStageOneDrawService(CompanionDbContext db) : IPlacementStageOneDrawService
{
    private const string ActiveLifecycle = "active";

    /// <summary>
    /// Draws one stage one for every objective skill of a certificate.
    /// </summary>
    /// <param name="certId">The certificate whose skills are examined.</param>
    /// <param name="contentTier">
    /// The paper's tier. A placement paper draws the stock of its own tier, exactly as a drill does:
    /// a production paper that drew demo stock would be INV-6 with a different spelling.
    /// </param>
    public async Task<PlacementStageOneDraw> DrawAsync(
        int certId,
        string contentTier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTier);

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Name, s.Modality })
            .ToListAsync(cancellationToken);

        var objective = skills
            .Where(skill => PlacementStageOneBlueprint.ObjectiveModalities
                .Select(ModalityCode)
                .Contains(skill.Modality, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // One read for every candidate of every objective skill: the pool is small relative to a
        // paper, and a per-skill query per band would be six round trips per skill to answer one question.
        var modalities = objective.Select(skill => skill.Modality).Distinct().ToList();
        var items = await db.Items
            .AsNoTracking()
            .Where(item => item.PlacementEligible
                && item.LifecycleStatus == ActiveLifecycle
                && item.ContentTier == contentTier
                && modalities.Contains(item.Modality))
            .Select(item => new
            {
                item.ItemId,
                item.Modality,
                item.CefrLevel,
                item.StimulusId,
                item.TopicId,
            })
            .ToListAsync(cancellationToken);

        // #254: "newest" is the authored order (VersionNumber), and GateContext states it once. A
        // hand-rolled ORDER BY here was a second definition of "which version is live", so a version
        // inserted out of order would have served a different row than gate one judged.
        var newestByItem = await LoadNewestVersionsAsync(items.Select(i => i.ItemId), cancellationToken);

        var plans = new List<PlacementStageOneSkillPlan>(objective.Count);
        foreach (var skill in objective)
        {
            var candidates = items
                .Where(item => string.Equals(item.Modality, skill.Modality, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.ItemId)
                .Select(item => new PlacementStageOneCandidate(item.ItemId, item.CefrLevel, item.StimulusId))
                .ToList();

            var selection = PlacementStageOneBlueprint.Select(candidates);

            plans.Add(new PlacementStageOneSkillPlan(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.Modality,
                selection.Bands,
                selection.Items
                    .Select((candidate, index) => new PlacementStageOneItem(
                        candidate.ItemId,
                        newestByItem.GetValueOrDefault(candidate.ItemId),
                        candidate.Level,
                        candidate.StimulusId,
                        index + 1))
                    .ToList()));
        }

        return new PlacementStageOneDraw(certId, contentTier, plans);
    }

    /// <summary>
    /// The live version of each item this draw can serve, via <see cref="GateContext.NewestPerItem{T}"/>
    /// so the ordering is stated once rather than restated per caller.
    /// </summary>
    private async Task<Dictionary<int, int?>> LoadNewestVersionsAsync(
        IEnumerable<int> itemIds, CancellationToken cancellationToken)
    {
        var ids = itemIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var versions = await db.ItemVersions
            .AsNoTracking()
            .Where(v => ids.Contains(v.ItemId))
            .Select(v => new { v.ItemId, v.VersionNumber, v.ItemVersionId })
            .ToListAsync(cancellationToken);

        return versions
            .GroupBy(v => v.ItemId)
            .ToDictionary(
                group => group.Key,
                group => (int?)GateContext.Newest(group, v => v.VersionNumber)?.ItemVersionId);
    }

    private static string ModalityCode(Modality modality) => modality.ToCode();
}

/// <summary>
/// One sitting's stage-one plan: a per-skill list of the items to serve and, per band, what was asked
/// for and what the pool could supply.
/// </summary>
public sealed record PlacementStageOneDraw(
    int CertId,
    string ContentTier,
    IReadOnlyList<PlacementStageOneSkillPlan> Skills)
{
    /// <summary>Every item the draw chose, across every objective skill.</summary>
    public int ItemCount => Skills.Sum(skill => skill.Items.Count);

    /// <summary>True when every skill's every band supplied its full blueprint count.</summary>
    public bool IsComplete => Skills.All(skill => skill.IsComplete);
}

/// <summary>One objective skill's stage-one share, with the deficits the caller must name.</summary>
public sealed record PlacementStageOneSkillPlan(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    IReadOnlyList<PlacementStageOneBandCount> Bands,
    IReadOnlyList<PlacementStageOneItem> Items)
{
    public bool IsComplete => Bands.All(band => band.Drawn >= band.Wanted);

    /// <summary>The bands this skill's stock could not fill, ascending by level.</summary>
    public IReadOnlyList<PlacementStageOneBandCount> Deficits =>
        Bands.Where(band => band.Drawn < band.Wanted).ToList();
}

/// <summary>One item the plan will serve: the item, the version pinned, its band and its paper position.</summary>
public sealed record PlacementStageOneItem(
    int ItemId,
    int? ItemVersionId,
    CefrLevel Level,
    int? StimulusId,
    int Position);
