using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// #206 / S-03: the review that decides whether a batch is promoted.
/// <para>
/// <b>Three properties this class exists to guarantee, in the order the stories state them.</b>
/// </para>
/// <list type="number">
/// <item><b>The sample is stratified (story 29).</b> One stratum per cell of the batch, and every cell
/// contributes at least one item. <see cref="StratifiedSample"/> owns the arithmetic; this class owns
/// the grouping, because "a cell" is <c>(TopicId, CefrLevel)</c> over the items that arrived in
/// <em>this</em> batch.</item>
/// <item><b>The solve is blind (story 30), and it is enforced where the key is READ.</b> The key is not
/// on <see cref="AuditSampleRow"/>, so listing the queue cannot reveal it. It leaves the database in
/// exactly one method, <see cref="RevealKeyAsync"/>, which refuses until <see cref="RecordSolveAsync"/>
/// has stored the reviewer's own answer. A view that hid the key while the API returned it would not be
/// blind at all - so the refusal is here, on the read, and <c>trg_ReviewSampleItems_SolveIsBlind</c>
/// closes the same door for writes this service does not make.</item>
/// <item><b>Promotion is the whole stratum (D17 "Promotion scope").</b> An approval promotes the sampled
/// items AND the unsampled remainder of every cell in the batch. Publishing only what was reviewed
/// leaves the pool at 10% of target volume - the failure the spec calls out by name.</item>
/// </list>
/// <para>
/// <b>The decision is derived, not chosen.</b> A reviewer records verdicts; <see cref="AuditStratumRule"/>
/// turns them into approve-or-quarantine. A "reject" button that ignored a confirmed key error would be
/// a second answer to story 31. <c>WrittenByService</c> is declared on every decision write, per ADR-0012.
/// </para>
/// </summary>
public sealed class StratifiedAuditService(
    CompanionDbContext db,
    TimeProvider clock,
    ILogger<StratifiedAuditService>? log = null)
{
    /// <summary>The staged state that has been judged and not yet published. Selection never reads it.</summary>
    private const string Validated = "validated";

    private const string Active = "active";

    private const string Quarantined = "quarantined";

    // ---- drawing ------------------------------------------------------------------------------

    /// <summary>
    /// Draws the sample for one batch: a stratum per cell, a floor of one item per cell, 10% of each.
    /// <para>
    /// <b>Idempotent by returning what exists.</b> A second draw would either duplicate the strata
    /// (refused by <c>UQ_ReviewStrata_Batch_Cell</c>) or silently re-sample over verdicts already
    /// recorded, so this returns the existing queue instead of moving an audit that is under way.
    /// </para>
    /// <para>
    /// Only <c>validated</c> items are drawn. A draft was never judged by the gates and an active one is
    /// already published; neither is this audit's subject.
    /// </para>
    /// </summary>
    public async Task<AuditQueue> DrawAsync(int batchId, int drawnBy, CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(batchId, cancellationToken);

        var existing = await db.ReviewStrata.AsNoTracking()
            .AnyAsync(s => s.BatchId == batchId, cancellationToken);
        if (existing)
        {
            return await GetQueueAsync(batchId, cancellationToken);
        }

        var staged = await StagedItemsAsync(batchId, cancellationToken);
        if (staged.Count == 0)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Batch {batchId} holds no staged item, so there is nothing to audit.", 409);
        }

        // One stratum per cell. Ordering by (topic, level) makes the draw's output stable, which is
        // what lets two reviewers be told the same items to solve.
        var cells = staged
            .GroupBy(i => (i.TopicId, Level: i.CefrLevel.Code))
            .OrderBy(g => g.Key.TopicId)
            .ThenBy(g => g.Key.Level, StringComparer.Ordinal)
            .ToList();

        var now = clock.GetUtcNow();
        foreach (var cell in cells)
        {
            // Order by ItemId so IndicesFor's spread picks the same items on every run.
            var items = cell.OrderBy(i => i.ItemId).ToList();
            var sampleSize = StratifiedSample.SizeFor(items.Count);
            var stratum = new ReviewStratum
            {
                BatchId = batchId,
                TopicId = cell.Key.TopicId,
                CefrLevel = items[0].CefrLevel,
                ItemCount = items.Count,
                SampleSize = sampleSize,
                Status = "pending",
            };
            db.ReviewStrata.Add(stratum);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var index in StratifiedSample.IndicesFor(items.Count, sampleSize))
            {
                db.ReviewSampleItems.Add(new ReviewSampleItem
                {
                    StratumId = stratum.StratumId,
                    BatchId = batchId,
                    ItemId = items[index].ItemId,
                    ItemVersionId = items[index].ItemVersionId,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        log?.LogInformation(
            "Batch {BatchId} ({Source}) sampled by account {DrawnBy}: {Strata} cells, {Staged} staged items.",
            batchId, batch.SourceName, drawnBy, cells.Count, staged.Count);

        return await GetQueueAsync(batchId, cancellationToken);
    }

    // ---- solving blind, then reading the key -------------------------------------------------

    /// <summary>
    /// W-01: the reviewer's own answer, recorded before anything can reveal the key.
    /// <para>
    /// <b>Refuses a re-solve once the key was read.</b> A second solve after a peek would let a reviewer
    /// correct themselves into agreement with the key, which is the anchoring story 30 exists to
    /// prevent - and it is the one case the database trigger also refuses, so the two cannot disagree.
    /// </para>
    /// </summary>
    public async Task RecordSolveAsync(
        int sampleItemId, string solvedLabel, int solvedBy, CancellationToken cancellationToken = default)
    {
        var sample = await FindSampleAsync(sampleItemId, cancellationToken);

        if (sample.VerdictAt is not null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Sample item {sampleItemId} already carries a verdict, so its solve is closed.", 409);
        }

        if (sample.KeyRevealedAt is not null)
        {
            throw new AdminItemException(
                ApiError.Codes.Forbidden,
                $"W-01: the authored key of sample item {sampleItemId} has already been read. A blind solve "
                + "cannot follow it; this item's verdict stands on the solve that was recorded first.", 409);
        }

        var label = solvedLabel.Trim().ToUpperInvariant();
        if (AuditLabels.IndexOf(label) < 0)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation, $"'{solvedLabel}' is not one of the four options (A-D).", 400);
        }

        sample.SolvedLabel = label;
        sample.SolvedAt = clock.GetUtcNow();
        sample.SolvedBy = solvedBy;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Story 30's enforcement point, stated plainly: <b>this is the only method that returns an authored
    /// key</b>, and it refuses until the reviewer's own answer is stored.
    /// <para>
    /// The precondition is here rather than in the Razor view because the view is not the boundary: an
    /// account that reads the API directly, or a second client written later, would skip a rule that
    /// lived in HTML. <see cref="AuditSampleRow"/> deliberately carries no key for the same reason - a
    /// queue listing that happened to include it would reveal every key at once.
    /// </para>
    /// <para>
    /// <see cref="ReviewSampleItem.KeyRevealedAt"/> is written here, so "was the key visible when they
    /// solved" is answerable afterwards by comparing it with <see cref="ReviewSampleItem.SolvedAt"/>.
    /// A boolean flag would not be: it could not say <em>when</em>.
    /// </para>
    /// </summary>
    public async Task<AuditKeyReveal> RevealKeyAsync(int sampleItemId, CancellationToken cancellationToken = default)
    {
        var sample = await FindSampleAsync(sampleItemId, cancellationToken);

        if (sample.SolvedLabel is null)
        {
            throw new AdminItemException(
                ApiError.Codes.Forbidden,
                $"W-01: sample item {sampleItemId} has no recorded solve. The authored key is not shown "
                + "until the reviewer has committed to their own answer, because a key read first anchors "
                + "the judgement it is meant to test.", 409);
        }

        var version = await db.ItemVersions.AsNoTracking()
            .Where(v => v.ItemVersionId == sample.ItemVersionId)
            .Select(v => new { v.KeyLabel, v.Explanation })
            .SingleAsync(cancellationToken);

        if (version.KeyLabel is null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Item version {sample.ItemVersionId} is a productive task and has no authored key.", 409);
        }

        var revealedAt = sample.KeyRevealedAt ?? clock.GetUtcNow();
        if (sample.KeyRevealedAt is null)
        {
            sample.KeyRevealedAt = revealedAt;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AuditKeyReveal(
            sample.SampleItemId, sample.ItemId, version.KeyLabel, version.Explanation,
            sample.SolvedLabel, revealedAt);
    }

    /// <summary>
    /// Records the reviewer's verdict on one item. Requires a solve, because a verdict on an unsolved
    /// item is a review of nothing.
    /// </summary>
    public async Task RecordVerdictAsync(
        int sampleItemId, AuditVerdict verdict, int reviewedBy, CancellationToken cancellationToken = default)
    {
        var sample = await FindSampleAsync(sampleItemId, cancellationToken);

        if (sample.SolvedLabel is null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Sample item {sampleItemId} has no recorded solve, so there is nothing to adjudicate.", 409);
        }

        if (sample.KeyRevealedAt is null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"The authored key of sample item {sampleItemId} has not been read. A verdict is a "
                + "comparison with the key, so it cannot be recorded before seeing it.", 409);
        }

        sample.Verdict = verdict switch
        {
            AuditVerdict.Correct => "correct",
            AuditVerdict.KeyError => "key_error",
            _ => "non_key_defect",
        };
        sample.VerdictAt = clock.GetUtcNow();
        sample.VerdictBy = reviewedBy;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- deciding ----------------------------------------------------------------------------

    /// <summary>
    /// Applies <see cref="AuditStratumRule"/> to the stratum's verdicts and moves every item in the cell
    /// with it.
    /// <para>
    /// <b>Every item, not the sampled ones.</b> On approval the sampled items and the unsampled
    /// remainder both go <c>validated -> active</c>; on quarantine both go to <c>quarantined</c>. A test
    /// with unsampled items asserts the second half, because a promotion that moved only the sample
    /// would satisfy the button and stall the pipeline - D17's own words.
    /// </para>
    /// <para>
    /// <b>ADM-02 is the database's, not this method's.</b> One <see cref="ReviewSampleDecision"/> is
    /// recorded per moved item, so <c>trg_ReviewSampleDecisions_NoSelfApproval</c> refuses an uploader,
    /// a last modifier or an author approving their own stock - from a direct service call, with no
    /// controller and no policy in the way.
    /// </para>
    /// </summary>
    public async Task<AuditDecisionResult> DecideAsync(
        int stratumId, int decidedBy, CancellationToken cancellationToken = default)
    {
        var stratum = await db.ReviewStrata
            .FirstOrDefaultAsync(s => s.StratumId == stratumId, cancellationToken)
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Stratum {stratumId} not found.", 404);

        if (!string.Equals(stratum.Status, "pending", StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Stratum {stratumId} is already '{stratum.Status}'. A taken decision is not re-taken; "
                + "re-verification is a separate act.", 409);
        }

        var verdicts = await db.ReviewSampleItems.AsNoTracking()
            .Where(s => s.StratumId == stratumId && s.Verdict != null)
            .Select(s => s.Verdict!)
            .ToListAsync(cancellationToken);

        var sampleSize = await db.ReviewSampleItems.AsNoTracking()
            .CountAsync(s => s.StratumId == stratumId, cancellationToken);

        if (verdicts.Count < sampleSize)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Stratum {stratumId} has {verdicts.Count} of {sampleSize} sampled items judged. Every "
                + "sampled item is judged before the cell is decided, or the decision is taken on a "
                + "fraction of the fraction.", 409);
        }

        var decision = AuditStratumRule.Decide(verdicts)
            ?? throw new AdminItemException(
                ApiError.Codes.Validation, $"Stratum {stratumId} has no verdict to decide on.", 409);

        var quarantining = string.Equals(decision.Status, "quarantined", StringComparison.Ordinal);

        // The whole cell, sample included. Read from ImportLog because that is the only record of which
        // item a record became (#205), and it is the same join the batch rule enforces.
        var itemIds = await db.Items
            .Where(i => i.TopicId == stratum.TopicId
                && i.CefrLevel == stratum.CefrLevel
                && db.ImportLogs.Any(l => l.BatchId == stratum.BatchId && l.ItemId == i.ItemId))
            .OrderBy(i => i.ItemId)
            .Select(i => i.ItemId)
            .ToListAsync(cancellationToken);

        var sampledItemIds = await db.ReviewSampleItems.AsNoTracking()
            .Where(s => s.StratumId == stratumId)
            .Select(s => s.ItemId)
            .ToListAsync(cancellationToken);

        var items = await db.Items.Where(i => itemIds.Contains(i.ItemId)).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        var moved = 0;
        var sampledMoved = 0;
        var alreadyDecided = 0;
        foreach (var item in items)
        {
            var from = item.LifecycleStatus;
            // Quarantine can reach an item that already reached service (telemetry's edge does the same);
            // promotion only ever moves a staged item, because an active one is already public.
            var eligible = quarantining
                ? from is Validated or Active
                : from == Validated;

            if (!eligible)
            {
                alreadyDecided += 1;
                continue;
            }

            item.LifecycleStatus = quarantining ? Quarantined : Active;
            moved += 1;
            if (sampledItemIds.Contains(item.ItemId))
            {
                sampledMoved += 1;
            }

            db.ReviewSampleDecisions.Add(new ReviewSampleDecision
            {
                BatchId = stratum.BatchId,
                ItemId = item.ItemId,
                ActorUserId = decidedBy,
                Decision = quarantining ? "adjudicate" : "clear",
                DecidedAt = now,
            });
        }

        stratum.Status = decision.Status;
        stratum.QuarantineReason = decision.Reason;
        stratum.DecidedAt = now;
        stratum.WrittenByService = DerivedFactWriters.StratumDecision;
        stratum.ProjectionVersion += 1;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is { } inner
            && inner.Message.Contains("ADM-02", StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Forbidden,
                "ADM-02: this account cannot decide a stratum for a batch or item it uploaded or last "
                + "modified, nor one whose newest version it wrote.", 403);
        }

        var sampled = sampledItemIds.Count;
        var result = new AuditDecisionResult(
            stratumId,
            decision.Status,
            decision.Reason,
            SampledPromoted: quarantining ? 0 : sampledMoved,
            UnsampledPromoted: quarantining ? 0 : moved - sampledMoved,
            Quarantined: quarantining ? moved : 0);

        log?.LogInformation(
            "Stratum {StratumId} of batch {BatchId} {Status}{Reason} by account {DecidedBy}: {Sampled} sampled "
            + "and {Unsampled} unsampled items moved, {Skipped} already decided.",
            stratumId, stratum.BatchId, decision.Status,
            decision.Reason is null ? string.Empty : $" ({decision.Reason})",
            decidedBy, sampled, Math.Max(0, moved - sampledMoved), alreadyDecided);

        return result;
    }

    // ---- reading -----------------------------------------------------------------------------

    /// <summary>
    /// The queue for one batch: one row per stratum, its sample, and the two counts the promotion
    /// sentence is made of. <b>No authored key is in this projection.</b>
    /// </summary>
    public async Task<AuditQueue> GetQueueAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(batchId, cancellationToken);

        var strata = await db.ReviewStrata.AsNoTracking()
            .Where(s => s.BatchId == batchId)
            .OrderBy(s => s.TopicId)
            .ThenBy(s => s.CefrLevel)
            .ToListAsync(cancellationToken);

        var samples = await db.ReviewSampleItems.AsNoTracking()
            .Where(s => s.BatchId == batchId)
            .OrderBy(s => s.SampleItemId)
            .ToListAsync(cancellationToken);

        var rows = new List<AuditStratumRow>(strata.Count);
        foreach (var stratum in strata)
        {
            var drawn = samples.Where(s => s.StratumId == stratum.StratumId).ToList();
            var items = await SampleRowsAsync(drawn, cancellationToken);
            var topicCode = await db.Topics.AsNoTracking()
                .Where(t => t.TopicId == stratum.TopicId)
                .Select(t => t.Code)
                .SingleAsync(cancellationToken);
            var modality = await db.Topics.AsNoTracking()
                .Where(t => t.TopicId == stratum.TopicId)
                .Select(t => t.Modality)
                .SingleAsync(cancellationToken);

            // The remainder is what the batch brought into this cell that the sample did not draw.
            // Counted from ItemCount - the size frozen at draw time - so a later record that arrives
            // does not silently enlarge the number the promotion sentence states.
            rows.Add(new AuditStratumRow(
                stratum.StratumId,
                stratum.BatchId,
                topicCode,
                stratum.CefrLevel.Code,
                modality,
                stratum.ItemCount,
                stratum.SampleSize,
                drawn.Count,
                Math.Max(0, stratum.ItemCount - drawn.Count),
                stratum.Status,
                stratum.QuarantineReason,
                stratum.DecidedAt,
                items));
        }

        return new AuditQueue(batchId, batch.SourceName, batch.Status, rows);
    }

    // ---- plumbing ----------------------------------------------------------------------------

    /// <summary>The four labels, in the order the item's options are rendered.</summary>
    private const string AuditLabels = "ABCD";
    private async Task<ImportBatch> LoadBatchAsync(int batchId, CancellationToken cancellationToken) =>
        await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
        ?? throw new AdminItemException(ApiError.Codes.Validation, $"Import batch {batchId} not found.", 404);

    private async Task<ReviewSampleItem> FindSampleAsync(int sampleItemId, CancellationToken cancellationToken) =>
        await db.ReviewSampleItems.FirstOrDefaultAsync(s => s.SampleItemId == sampleItemId, cancellationToken)
        ?? throw new AdminItemException(
            ApiError.Codes.Validation, $"Sample item {sampleItemId} not found.", 404);

    /// <summary>
    /// The items this batch brought in that are staged. The join is <c>ImportLog</c>'s, because that is
    /// the only record of which item a record became - <c>Items.BatchTag</c> is the file's own tag, so
    /// two uploads of one file collide on it and a file declaring none matches nothing.
    /// </summary>
    private async Task<List<StagedCell>> StagedItemsAsync(int batchId, CancellationToken cancellationToken)
    {
        var items = await db.Items.AsNoTracking()
            .Where(i => i.LifecycleStatus == Validated
                && db.ImportLogs.Any(l => l.BatchId == batchId && l.ItemId == i.ItemId))
            .Select(i => new { i.ItemId, i.TopicId, i.CefrLevel })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return [];
        }

        var itemIds = items.Select(i => i.ItemId).ToList();
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => itemIds.Contains(v.ItemId))
            .Select(v => new { v.ItemId, v.ItemVersionId, v.VersionNumber })
            .ToListAsync(cancellationToken);

        // #254's ordering, asked once: the newest version is the highest VersionNumber, and a sample
        // pins that version's key.
        var newest = GateContext.NewestPerItem(versions, v => v.ItemId, v => v.VersionNumber)
            .ToDictionary(v => v.ItemId, v => v.ItemVersionId);

        var staged = new List<StagedCell>(items.Count);
        foreach (var item in items)
        {
            if (newest.TryGetValue(item.ItemId, out var versionId))
            {
                staged.Add(new StagedCell(item.ItemId, item.TopicId, item.CefrLevel, versionId));
            }
        }

        return staged;
    }

    private async Task<IReadOnlyList<AuditSampleRow>> SampleRowsAsync(
        IReadOnlyList<ReviewSampleItem> drawn, CancellationToken cancellationToken)
    {
        if (drawn.Count == 0)
        {
            return [];
        }

        var versionIds = drawn.Select(s => s.ItemVersionId).ToList();
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => versionIds.Contains(v.ItemVersionId))
            .Select(v => new { v.ItemVersionId, v.Stem, v.OptionA, v.OptionB, v.OptionC, v.OptionD })
            .ToListAsync(cancellationToken);
        var byVersion = versions.ToDictionary(v => v.ItemVersionId);

        var rows = new List<AuditSampleRow>(drawn.Count);
        foreach (var sample in drawn)
        {
            // A sample can only name a version that exists (FK), so the default is unreachable in
            // practice; rendering an empty stem beats throwing on a page nobody can load.
            var version = byVersion.GetValueOrDefault(sample.ItemVersionId);
            rows.Add(new AuditSampleRow(
                sample.SampleItemId,
                sample.ItemId,
                version?.Stem ?? string.Empty,
                version?.OptionA ?? string.Empty,
                version?.OptionB ?? string.Empty,
                version?.OptionC ?? string.Empty,
                version?.OptionD ?? string.Empty,
                sample.SolvedLabel,
                sample.SolvedAt,
                sample.KeyRevealedAt,
                sample.KeyRevealedAt is not null,
                sample.Verdict));
        }

        return rows;
    }

    /// <summary>One staged item and the version the sample will pin.</summary>
    private sealed record StagedCell(int ItemId, int TopicId, CefrLevel CefrLevel, int ItemVersionId);
}
