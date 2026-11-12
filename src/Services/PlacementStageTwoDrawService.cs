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
/// #215 / D7: stage two's draw — the routed pair of bands, the per-pair counts, and the same-band
/// cascade, over the real pool.
/// <para>
/// <b>This class supplies candidates; <see cref="PlacementStageTwoBlueprint"/> owns every rule.</b> The
/// routing decision, the counts and the four tiers of the cascade are arithmetic over a list, so they are
/// testable without a database and cannot drift into a second implementation here. What this class adds is
/// the three facts only the database has: which items are active placement stock of the paper's own tier
/// (INV-6, CNT-24), how many times <i>this</i> learner has answered each one, and which items stage one
/// already served in the same administration.
/// </para>
/// <para>
/// <b>Why the draw is not wired into a sitting yet.</b> Two independent reasons, both checkable:
/// (1) the paper roster still reads the legacy <c>Questions</c> table, and #250 owns moving it to
/// <c>ExamItems</c> — the gap #214 recorded; (2) nothing records a stage index or a per-skill stage-one
/// score, so there is no input to route on: <c>ExamAttempts</c> and <c>PlacementTests</c> carry no stage
/// column, and an attempt's <c>AttemptSkillScores.RawScore</c> does not say which stage produced it. The
/// seam is finished and tested; #250 wires both stages once the roster and the stage index exist.
/// </para>
/// </summary>
public sealed class PlacementStageTwoDrawService(CompanionDbContext db) : IPlacementStageTwoDrawService
{
    private const string ActiveLifecycle = "active";

    /// <summary>
    /// Draws stage two for every objective skill that has a stage-one score.
    /// </summary>
    /// <param name="userId">Whose history decides what is unseen. Never a global "seen" flag.</param>
    /// <param name="certId">The certificate whose skills are examined.</param>
    /// <param name="contentTier">The paper's own tier: a placement that drew demo stock would be INV-6.</param>
    /// <param name="stageOneScoreBySkillId">
    /// The stage-one number-correct per objective skill. A skill absent here has no stage-one result, and
    /// therefore no route — it is skipped rather than routed to a guess.
    /// </param>
    /// <param name="servedItemIds">
    /// Items stage one already served in this administration. D7's "rather than repeating an item" is
    /// about one administration, so an item already answered ten minutes ago cannot come back here.
    /// </param>
    /// <param name="preferredTopicBySkillId">
    /// The topic a skill would rather draw from, usually the weakest one from the stage-one topic signal
    /// (#219). Null means no preference: every eligible topic is then a primary source.
    /// </param>
    public async Task<PlacementStageTwoDraw> DrawAsync(
        int userId,
        int certId,
        string contentTier,
        IReadOnlyDictionary<int, int> stageOneScoreBySkillId,
        IReadOnlyList<int>? servedItemIds = null,
        IReadOnlyDictionary<int, int>? preferredTopicBySkillId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTier);
        ArgumentNullException.ThrowIfNull(stageOneScoreBySkillId);

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

        // One read for every candidate of every objective skill: the pool is small relative to a paper,
        // and a per-band query would be six round trips per skill to answer one question.
        var modalities = objective.Select(skill => skill.Modality).Distinct().ToList();

        var items = await db.Items
            .AsNoTracking()
            .Where(item => item.LifecycleStatus == ActiveLifecycle
                && item.ContentTier == contentTier
                && modalities.Contains(item.Modality))
            .Select(item => new
            {
                item.ItemId,
                item.Modality,
                item.CefrLevel,
                item.StimulusId,
                item.TopicId,
                item.PlacementEligible,
            })
            .ToListAsync(cancellationToken);

        // #254: "newest" is the authored order (VersionNumber), stated once in GateContext. A
        // hand-rolled ORDER BY here was a second definition of "which version is live".
        var newestByItem = await LoadNewestVersionsAsync(items.Select(i => i.ItemId), cancellationToken);

        // "Seen" is this learner's own practice history and nothing else. A global count would let one
        // learner's drilling exhaust another learner's paper.
        var seen = await db.PracticeAnswers
            .AsNoTracking()
            .Where(answer => answer.Session.UserId == userId && answer.ItemId != null)
            .GroupBy(answer => answer.ItemId!.Value)
            .Select(group => new
            {
                ItemId = group.Key,
                TimesSeen = group.Count(),
                LastSeenAt = group.Max(answer => answer.AnsweredAt),
            })
            .ToListAsync(cancellationToken);

        var history = seen.ToDictionary(
            row => row.ItemId,
            row => (row.TimesSeen, LastSeenAt: row.LastSeenAt is DateTime at
                ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
                : (DateTimeOffset?)null));

        var served = servedItemIds is { Count: > 0 } ids ? ids.ToHashSet() : [];

        var plans = new List<PlacementStageTwoSkillPlan>(objective.Count);
        foreach (var skill in objective)
        {
            if (!stageOneScoreBySkillId.TryGetValue(skill.SkillId, out var score))
            {
                continue;
            }

            var route = PlacementStageTwoBlueprint.Route(score);
            var preferredTopic = preferredTopicBySkillId is not null
                && preferredTopicBySkillId.TryGetValue(skill.SkillId, out var topic)
                    ? topic
                    : (int?)null;

            var candidates = items
                .Where(item => string.Equals(item.Modality, skill.Modality, StringComparison.OrdinalIgnoreCase))
                .Where(item => !served.Contains(item.ItemId))
                .Where(item => item.CefrLevel == route.Lower || item.CefrLevel == route.Upper)
                .OrderBy(item => item.ItemId)
                .Select(item =>
                {
                    history.TryGetValue(item.ItemId, out var past);
                    return new PlacementStageTwoStock(
                        item.ItemId,
                        item.CefrLevel,
                        item.TopicId,
                        item.StimulusId,
                        item.PlacementEligible,
                        past.TimesSeen,
                        past.LastSeenAt);
                })
                .ToList();

            var selection = PlacementStageTwoBlueprint.Select(candidates, route, preferredTopic);

            plans.Add(new PlacementStageTwoSkillPlan(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.Modality,
                score,
                route,
                selection.Bands,
                selection.Items
                    .Select((item, index) => new PlacementStageTwoPlanItem(
                        item.ItemId,
                        newestByItem.GetValueOrDefault(item.ItemId),
                        item.Level,
                        item.StimulusId,
                        item.TopicId,
                        item.Tier,
                        index + 1))
                    .ToList()));
        }

        return new PlacementStageTwoDraw(certId, contentTier, plans);
    }

    /// <summary>
    /// The live version of each item this draw can serve, via <see cref="GateContext.Newest{T}"/> so the
    /// ordering is stated once rather than restated per caller.
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

    /// <summary>
    /// Refuses a sitting whose routed bands cannot supply a measurable paper, <b>before any attempt row
    /// exists</b>. This is a read: it creates nothing, so a refusal cannot leave a half-started sitting
    /// behind, which is what the ticket's "before" means.
    /// </summary>
    /// <exception cref="PlacementException">
    /// <see cref="PlacementException.PlacementStockShort"/>, naming every missing cell by skill, band and
    /// shortfall. A degraded form is not refused — it runs and records its own counts.
    /// </exception>
    public async Task<PlacementStageTwoDraw> EnsureSittableAsync(
        int userId,
        int certId,
        string contentTier,
        IReadOnlyDictionary<int, int> stageOneScoreBySkillId,
        IReadOnlyList<int>? servedItemIds = null,
        IReadOnlyDictionary<int, int>? preferredTopicBySkillId = null,
        CancellationToken cancellationToken = default)
    {
        var draw = await DrawAsync(
            userId, certId, contentTier, stageOneScoreBySkillId,
            servedItemIds, preferredTopicBySkillId, cancellationToken);

        if (!draw.IsRefused)
        {
            return draw;
        }

        var missing = string.Join("; ", draw.Refusals.Select(refusal =>
            $"{refusal.SkillCode} at {refusal.Level.Code} has {refusal.Drawn} of {refusal.Wanted} "
            + $"({refusal.Missing} short, {PlacementStageTwoBlueprint.MinimumStageTwoItemsAtABand} is the floor)"));

        throw new PlacementException(
            PlacementException.PlacementStockShort,
            $"This certificate's pool cannot supply a measurable stage two, so no sitting was started. {missing}.");
    }

    private static string ModalityCode(Modality modality) => modality.ToCode();
}

/// <summary>
/// One sitting's stage-two plan: a per-skill list of the items to serve, the pair its score routed to,
/// and per band what was asked for and what the pool could supply.
/// </summary>
public sealed record PlacementStageTwoDraw(
    int CertId,
    string ContentTier,
    IReadOnlyList<PlacementStageTwoSkillPlan> Skills)
{
    public int ItemCount => Skills.Sum(skill => skill.Items.Count);

    public bool IsComplete => Skills.Count > 0 && Skills.All(skill => skill.IsComplete);

    /// <summary>Every band short of its full count, across every skill.</summary>
    public IReadOnlyList<PlacementStageTwoDeficit> Deficits =>
        Skills.SelectMany(skill => skill.Deficits).ToList();

    /// <summary>Every band below D10's floor: the sitting must not start.</summary>
    public IReadOnlyList<PlacementStageTwoDeficit> Refusals =>
        Skills.SelectMany(skill => skill.Refusals).ToList();

    public bool IsRefused => Refusals.Count > 0;

    /// <summary>
    /// True when the paper runs with fewer items than a full form. The sitting records this — with the
    /// per-band counts <see cref="Deficits"/> carries — rather than repeating an item to reach the count.
    /// </summary>
    public bool IsDegraded => !IsRefused && Deficits.Count > 0;

    /// <summary>The shortfall in one line, for a stored warning or a log entry.</summary>
    public string? DegradedDescription => IsDegraded
        ? string.Join(", ", Deficits.Select(d => $"{d.SkillCode} {d.Level.Code} {d.Drawn}/{d.Wanted}"))
        : null;
}

/// <summary>One objective skill's stage-two share, with the deficits the caller must name.</summary>
public sealed record PlacementStageTwoSkillPlan(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    int StageOneScore,
    PlacementStageTwoRoute Route,
    IReadOnlyList<PlacementStageTwoBandCount> Bands,
    IReadOnlyList<PlacementStageTwoPlanItem> Items)
{
    public bool IsComplete => Bands.All(band => band.Drawn >= band.Wanted);

    /// <summary>The bands this skill's stock could not fill, ascending by level.</summary>
    public IReadOnlyList<PlacementStageTwoDeficit> Deficits =>
        Bands.Where(band => band.Drawn < band.Wanted)
            .Select(band => new PlacementStageTwoDeficit(SkillCode, band.Level, band.Wanted, band.Drawn))
            .ToList();

    /// <summary>The bands below D10's floor, which cannot be measured at all.</summary>
    public IReadOnlyList<PlacementStageTwoDeficit> Refusals =>
        Bands.Where(band => band.Drawn < PlacementStageTwoBlueprint.MinimumStageTwoItemsAtABand)
            .Select(band => new PlacementStageTwoDeficit(SkillCode, band.Level, band.Wanted, band.Drawn))
            .ToList();
}

/// <summary>One missing cell: which skill, which band, and how far short it came.</summary>
public sealed record PlacementStageTwoDeficit(
    string SkillCode,
    CefrLevel Level,
    int Wanted,
    int Drawn)
{
    public int Missing => Math.Max(0, Wanted - Drawn);
}

/// <summary>
/// One item the plan will serve: the item, the version pinned, its band and topic, which tier of the
/// cascade supplied it, and its position on the paper.
/// </summary>
public sealed record PlacementStageTwoPlanItem(
    int ItemId,
    int? ItemVersionId,
    CefrLevel Level,
    int? StimulusId,
    int TopicId,
    PlacementStageTwoTier Tier,
    int Position);
