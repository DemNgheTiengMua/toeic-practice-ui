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
/// #235 / D11: <b>the one selector.</b> A path step and a free drill differ only in origin (PLC-57), so
/// both call this: same filter, same ordering, same cooldown, same newest-version rule.
/// <para>
/// The filter is <c>(cert, skill, topic, level, placement_eligible = false)</c> plus the lifecycle and
/// tier rules #190 already states. <b>A pool item carries no certificate</b>, so "filtered by
/// certificate" is resolved exactly: the certificate declares which skills it examines, and an item is
/// servable only if its modality is one of them.
/// </para>
/// <para>
/// Selection is a <b>read</b>. Nothing here writes a row (ADR-0012): the cooldown is derived from the
/// answers already stored, so there is no second writer of a derived fact.
/// </para>
/// </summary>
public sealed class ItemSelector(CompanionDbContext db, TimeProvider? clock = null)
{
    /// <summary>The one state selection reads. Named, so a value added to <c>CK_Items_Lifecycle</c>
    /// cannot start being served by "not this".</summary>
    public const string ActiveLifecycle = "active";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// The servable items of one cell, ranked least-seen → least-recently-seen → item id.
    /// <paramref name="contentTier"/> is null for a path step (a step has no tier of its own) and the
    /// drill's tier for a free drill.
    /// </summary>
    public async Task<PoolSelection> SelectAsync(
        int userId, int certId, IReadOnlyCollection<int> skillIds, string modality, int topicId,
        CefrLevel level, string? contentTier, int size, CancellationToken cancellationToken = default)
    {
        var candidates = await LoadCandidatesAsync(
            userId, certId, skillIds, modality, topicId, level, contentTier, cancellationToken);

        return PoolRanking.Select(candidates, _clock.GetUtcNow(), size);
    }

    /// <summary>
    /// D11 / PLC-60: <b>a path step is never blocked by a cooldown.</b> The step's own cell is drawn
    /// first; when <b>nothing servable is left in it because its items are cooling</b>, the servable
    /// items of sibling topics in the same (skill, level) fill the step and the result says so; and
    /// only when the whole band is cooling does it lift the cooldown on the least-recently-seen item of
    /// the step's own cell. Returning nothing is the one thing it must not do while stock exists.
    /// <para>
    /// A cell that simply holds fewer items than the tier asks for is <b>not</b> a substitution: it is
    /// an honestly short draw from its own cell, which is what "returns what exists and is never padded"
    /// means. Substituting for mere shortness would serve a sibling topic every time a 30-item cell was
    /// asked for 15 and could only supply 10, and would claim a cooldown detour that never happened.
    /// </para>
    /// </summary>
    public async Task<StepDraw> DrawForStepAsync(
        int userId, int certId, IReadOnlyCollection<int> skillIds, string modality, int topicId,
        CefrLevel level, int size, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();

        var own = await LoadCandidatesAsync(
            userId, certId, skillIds, modality, topicId, level, null, cancellationToken);

        var ownSelection = PoolRanking.Select(own, now, size);
        if (!ownSelection.IsEmpty)
        {
            return new StepDraw(ownSelection.Items, StepDrawOutcome.Served);
        }

        var siblingTopics = await db.Topics
            .AsNoTracking()
            .Where(t => t.Modality == modality && t.TopicId != topicId)
            .OrderBy(t => t.TopicId)
            .Select(t => t.TopicId)
            .ToListAsync(cancellationToken);

        var picked = new List<PoolCandidate>();
        foreach (var sibling in siblingTopics)
        {
            if (picked.Count >= size)
            {
                break;
            }

            var extra = PoolRanking.Select(
                await LoadCandidatesAsync(userId, certId, skillIds, modality, sibling, level, null, cancellationToken),
                now, size - picked.Count);

            picked.AddRange(extra.Items.Where(e => picked.All(p => p.ItemId != e.ItemId)));
        }

        if (picked.Count > 0)
        {
            return new StepDraw(picked, StepDrawOutcome.SubstitutedSiblingTopic);
        }

        // The whole band is cooling. A step must still be executable, so the item the learner has not
        // seen for longest is served again rather than refusing the day's practice. That order is the
        // spec's own ("the oldest-exposed item in the cell ordered by last_seen_at ASC") and is
        // deliberately NOT <see cref="PoolRanking.Rank"/>'s: times_seen would pick the most-drilled item,
        // which is the opposite of what a forced repeat should choose.
        var fallingBack = own
            .OrderBy(c => c.LastSeenAt ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.ItemId)
            .Take(size)
            .ToList();

        return fallingBack.Count == 0
            ? new StepDraw([], StepDrawOutcome.Empty)
            : new StepDraw(fallingBack, StepDrawOutcome.CooldownLifted);
    }

    /// <summary>
    /// One cell's active items, with this learner's answer statistics, newest version per item. The
    /// statistics come from <c>PracticeAnswers</c> and never from <c>UserQuestionHistories</c>: that
    /// table is keyed by the legacy <c>QuestionId</c>, so comparing it to an <c>ItemId</c> matched the
    /// wrong rows and no pool answer ever wrote one.
    /// <para>
    /// <b>The certificate filter.</b> A pool item carries no <c>CertId</c> by design (#184: an item is
    /// not written "for TOEIC"), so "filtered by certificate" is enforced through the certificate's own
    /// declared skills: an item is servable only when the certificate examines its modality. A
    /// certificate that declares no skill at all is unconstrained rather than starving every drill —
    /// it has stated no boundary to enforce.
    /// </para>
    /// </summary>
    private async Task<List<PoolCandidate>> LoadCandidatesAsync(
        int userId, int certId, IReadOnlyCollection<int> skillIds, string modality, int topicId,
        CefrLevel level, string? contentTier, CancellationToken cancellationToken)
    {
        var certModalities = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId && (skillIds.Count == 0 || skillIds.Contains(s.SkillId)))
            .Select(s => s.Modality)
            .ToListAsync(cancellationToken);

        if (certModalities.Count > 0 && !certModalities.Contains(modality))
        {
            return [];
        }

        var items = db.Items
            .AsNoTracking()
            .Where(i => i.TopicId == topicId
                && i.Modality == modality
                && i.CefrLevel == level
                && i.LifecycleStatus == ActiveLifecycle
                && !i.PlacementEligible);

        if (contentTier is not null)
        {
            items = items.Where(i => i.ContentTier == contentTier);
        }

        var rows = await items
            .Select(i => new
            {
                i.ItemId,
                i.ItemType,
                i.TopicId,
                TimesSeen = db.PracticeAnswers.Count(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId),
                LastSeenAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
                LastWrongAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId && pa.IsCorrect == false)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
                LastCorrectAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId && pa.IsCorrect == true)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var itemIds = rows.Select(r => r.ItemId).ToList();
        // #254: "newest" is the authored order (VersionNumber), and GateContext states it once. Asking
        // for the highest ItemVersionId here served a different row than gate one judged whenever a
        // version was inserted out of order.
        var versions = await db.ItemVersions
            .AsNoTracking()
            .Where(v => itemIds.Contains(v.ItemId))
            .Select(v => new { v.ItemId, v.VersionNumber, v.ItemVersionId })
            .ToListAsync(cancellationToken);

        var newest = ItemVersionQuery.NewestPerItem(versions, v => v.ItemId, v => v.VersionNumber)
            .ToDictionary(v => v.ItemId, v => v.ItemVersionId);

        return rows
            .Where(r => newest.ContainsKey(r.ItemId))
            .Select(r => new PoolCandidate(
                r.ItemId,
                newest[r.ItemId],
                r.TopicId,
                r.ItemType,
                r.TimesSeen,
                Utc(r.LastSeenAt),
                Utc(r.LastWrongAt),
                Utc(r.LastCorrectAt)))
            .ToList();
    }

    /// <summary><c>PracticeAnswers.AnsweredAt</c> is a legacy <c>DATETIME2</c> with no offset. Rows are
    /// written with <c>DateTime.UtcNow</c>, so the kind is UTC and saying so is not a conversion.</summary>
    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
}

/// <summary>#235: what a path step's draw did, so the caller can say it out loud.</summary>
public enum StepDrawOutcome
{
    /// <summary>The step's own cell filled it.</summary>
    Served,

    /// <summary>The step's cell was cooling, so sibling topics of the same band filled it (PLC-60).</summary>
    SubstitutedSiblingTopic,

    /// <summary>Every topic in the band is cooling, so the longest-unseen item of the own cell was
    /// served again rather than blocking the step.</summary>
    CooldownLifted,

    /// <summary>The band holds no active index at all — a stock gap, not a cooldown.</summary>
    Empty,
}

/// <summary>#235: one step's draw and why it is the draw it is.</summary>
public sealed record StepDraw(IReadOnlyList<PoolCandidate> Items, StepDrawOutcome Outcome);
