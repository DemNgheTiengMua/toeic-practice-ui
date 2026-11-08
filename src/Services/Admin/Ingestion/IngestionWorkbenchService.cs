using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// #205 / S-02: the batch, its per-record verdicts, and one corrected record re-judged.
/// <para>
/// <b>Not a second ingestion seam.</b> This class answers "record what happened to every record, and
/// let one of them be tried again". Whether a record passes is <see cref="PoolIngestionGates"/>'s
/// answer, and what an accepted bundle writes is <see cref="PoolIngestionWriter"/>'s - both are called
/// here exactly as <see cref="PoolIngestionService"/> calls them. Re-implementing either would be a
/// second answer to a question this repo has already had disagree with itself.
/// </para>
/// <para>
/// <b>No code path marks a verdict accepted.</b> A verdict row is written only from the result of a
/// fresh judgement, so a re-submission that "cleared the error flag" cannot exist: there is nothing to
/// clear. That is criterion 3's guard, and it is structural rather than a rule to remember.
/// </para>
/// </summary>
public sealed class IngestionWorkbenchService(
    CompanionDbContext db,
    TimeProvider clock,
    PoolIngestionService ingestion,
    ILogger<IngestionWorkbenchService>? log = null)
{
    /// <summary>
    /// The serializer settings the rebuilt group is written with. Indented because the reader's line
    /// numbers are newline counts and the gates key per-record results by line: a compact group puts
    /// every child on one line and they collide.
    /// </summary>
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// #207: the rule code a discarded record's verdict carries. A discard is a verdict like any other
    /// - it is appended at the next attempt rather than deleting the refusal it replaces - so it needs a
    /// code the page can recognise and the grouping can exclude.
    /// </summary>
    public const string DiscardRule = "workbench.discarded";
    // ---- uploading ---------------------------------------------------------------------------

    /// <summary>
    /// Accepts a file for one skill, records the batch and one verdict per record, and stages every
    /// accepted record as <c>validated</c>.
    /// <para>
    /// A partly valid file is the normal shape, not an error: the accepted records are written and the
    /// refused ones are listed. The batch's status says which happened, and #207 makes that a single
    /// derived rule rather than this method's private one: <c>done</c> only when <b>no record is still
    /// refused</b>, so a mixed file is <c>failed</c> too - the difference between "nothing landed" and
    /// "some landed, some refused" is the accepted count, not the word.
    /// </para>
    /// </summary>
    public async Task<WorkbenchBatchResult> UploadAsync(
        string skill,
        Stream content,
        string sourceName,
        int uploadedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var batch = new ImportBatch
        {
            // Pool stock names no certificate (CNT-01); the column is nullable for exactly this.
            CertId = null,
            SourceName = Clip(Path.GetFileName(sourceName), 260),
            Status = "pending",
            UploadedBy = uploadedBy,
            CreatedAt = clock.GetUtcNow(),
            // Kept so a corrected record can be re-judged against its own group: gate two profiles the
            // shared passage and gate four's span rule is about siblings. The same document supplies
            // the record text back to the correction form.
            SourceDocument = Encoding.UTF8.GetString(bytes),
        };

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        // The writing half of the seam, not the judging half: an accepted record must reach the pool,
        // and the seam stages it as `validated` so the audit has something to clear.
        var judged = await ingestion.IngestAsync(skill, bytes, cancellationToken);

        await RecordVerdictsAsync(batch.BatchId, judged.Records, attempt: 1, cancellationToken);

        batch.CompletedAt = clock.GetUtcNow();

        // #207: the status is derived from the record verdicts in one place, so an upload, a
        // re-submission and a discard cannot disagree about what state the batch is in.
        await RecomputeStatusAsync(batch, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        log?.LogInformation(
            "Batch {BatchId} ({Source}) by account {UploadedBy}: {Accepted} accepted, {Rejected} refused.",
            batch.BatchId, batch.SourceName, uploadedBy, judged.Accepted.Count, judged.Rejected.Count);

        return new WorkbenchBatchResult(
            batch.BatchId,
            batch.SourceName,
            skill.Trim().ToLowerInvariant(),
            batch.Status,
            uploadedBy,
            batch.CreatedAt,
            judged.Records,
            judged.ItemsWritten,
            judged.VersionsWritten);
    }

    // ---- re-submitting one record ------------------------------------------------------------

    /// <summary>
    /// AC3: one corrected record, re-judged, without the rest of the file.
    /// <para>
    /// <b>What is corrected is the record, not the file.</b> Its group is rebuilt from the batch's
    /// stored document, so the passage gate two profiles and the siblings gate four measures against
    /// are the ones the first judgement used. A corrected record judged in isolation would be judged
    /// by a weaker check - no passage, no siblings - and reported as the same one.
    /// </para>
    /// <para>
    /// The verdicts are written at <c>Attempt + 1</c> and only for this line, so the rejection that was
    /// corrected stays readable beside its correction.
    /// </para>
    /// </summary>
    public async Task<IngestionRecordResult> ResubmitAsync(
        int batchId,
        int line,
        string correctedRecordJson,
        int submittedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctedRecordJson);

        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Import batch {batchId} not found.", 404);

        if (string.IsNullOrWhiteSpace(batch.SourceDocument))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Batch {batchId} kept no copy of its upload, so the record at line {line} cannot be "
                + "re-judged against its group. Upload the file again.", 409);
        }

        var rebuilt = RebuildWithCorrectedRecord(batch.SourceDocument, line, correctedRecordJson);

        // The same gates the upload ran, over the corrected record in its original group. Nothing here
        // inspects the corrected text and nothing accepts it: the verdict is what the four gates said,
        // and an accepted record is written by the same writer the upload uses.
        var verdict = await ingestion.RejudgeOneAsync(
            rebuilt.Skill ?? "reading", rebuilt.FileJson, rebuilt.RecordId, line, cancellationToken);

        var attempt = await NextAttemptAsync(batchId, cancellationToken);
        await RecordVerdictsAsync(batchId, [verdict], attempt, cancellationToken);

        // The account that looked again is the one ADM-02's second arm bars from clearing the result,
        // so it is recorded on the batch rather than left to the log.
        batch.LastModifiedBy = submittedBy;

        // #207 AC4: a batch that ended `failed` because every record was refused stops being `failed`
        // once its last refusal is corrected. The status is derived, never assigned by the caller, so
        // the re-submission cannot forget it the way it did before.
        await RecomputeStatusAsync(batch, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        log?.LogInformation(
            "Batch {BatchId} line {Line} re-judged by account {SubmittedBy} at attempt {Attempt}: {Outcome}.",
            batchId, line, submittedBy, attempt, verdict.Accepted ? "accepted" : "refused");

        return verdict;
    }

    // ---- discarding one record ---------------------------------------------------------------

    /// <summary>
    /// #207 AC3: withdraw one refused record from the batch without touching anything else.
    /// <para>
    /// <b>What a discard is.</b> A record is a line of the batch's stored document and a verdict is an
    /// <c>ImportLog</c> row. Discarding appends one more verdict - at <c>Attempt + 1</c>, like a
    /// re-submission - saying the record was withdrawn by this account. It is <em>not</em> a delete:
    /// the refusals that led here stay readable, because they are the audit trail of why the record
    /// was dropped, and <c>trg_ReviewSampleDecisions_NoSelfApproval</c> refuses deletes for exactly
    /// this reason. Nothing in the stored document is rewritten either, so the file the batch judged
    /// remains the file it judged.
    /// </para>
    /// <para>
    /// <b>What it never touches.</b> Accepted records and the staged items they produced. A discard
    /// names one line, and an accepted line is refused here rather than withdrawn quietly: its item is
    /// already staged and may already be referenced, so removing it is a lifecycle decision
    /// (<c>retired</c>), not a remediation one. A discarded line names no item, so the item tables are
    /// not written at all.
    /// </para>
    /// </summary>
    public async Task<IngestionRecordResult> DiscardAsync(
        int batchId,
        int line,
        string? reason,
        int discardedBy,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches
            .FirstOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Import batch {batchId} not found.", 404);

        var latest = await LatestVerdictAsync(batchId, line, cancellationToken)
            ?? throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Batch {batchId} holds no record at line {line}.", 404);

        if (string.Equals(latest.RuleCode, DiscardRule, StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"The record at line {line} was already discarded. Discarding it again would record a "
                + "second withdrawal nobody made.", 409);
        }

        if (string.Equals(latest.Level, "info", StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"The record at line {line} was accepted, so it has no rejection to discard. Its staged "
                + "item is withdrawn through the item's lifecycle, not by discarding the record.", 409);
        }

        // The reason is kept in the message rather than in a column of its own: a discard is one row
        // of the same audit trail, and a nullable `DiscardReason` would be a second place a verdict
        // lives. The rule it was refused under is carried along so the page can still say why.
        var refusedUnder = await db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batchId && l.RowNo == line && l.Attempt == latest.Attempt && l.Level == "error")
            .OrderBy(l => l.LogId)
            .Select(l => l.RuleCode)
            .ToListAsync(cancellationToken);

        var note = string.IsNullOrWhiteSpace(reason)
            ? "Record discarded by staff; no reason given."
            : $"Record discarded by staff: {reason.Trim()}";

        db.ImportLogs.Add(new ImportLog
        {
            BatchId = batchId,
            RowNo = line,
            Level = "warn",
            Message = Clip(
                refusedUnder.Count == 0
                    ? note
                    : $"{note} (refused under {string.Join(", ", refusedUnder)}.)",
                1000),
            RuleCode = DiscardRule,
            Attempt = latest.Attempt + 1,
            ItemId = null,
            LoggedAt = clock.GetUtcNow(),
        });

        batch.LastModifiedBy = discardedBy;

        // #207 AC4: withdrawing the last outstanding refusal settles the batch, and the same derivation
        // is what a re-submission uses. A discard must not leave `failed` behind.
        await db.SaveChangesAsync(cancellationToken);
        await RecomputeStatusAsync(batch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        log?.LogInformation(
            "Batch {BatchId} line {Line} discarded by account {DiscardedBy}.", batchId, line, discardedBy);

        return new IngestionRecordResult(line, string.Empty, Accepted: false, Refusals: []);
    }

    // ---- the audit's clearance ---------------------------------------------------------------

    /// <summary>
    /// AC2's second half: <c>validated -> active</c>, the transition that makes an imported item
    /// servable.
    /// <para>
    /// <b>What the two states mean.</b> <c>draft</c> is "not yet judged by the four gates";
    /// <c>validated</c> is "the gates passed and the audit has not cleared it"; <c>active</c> is the
    /// one state every pool selector reads. CNT-42 names the path, and this is its only application
    /// caller - so an item reaches a learner here or nowhere.
    /// </para>
    /// <para>
    /// <b>Why a draft cannot be cleared.</b> Only <c>validated</c> proves the gates ran. Accepting any
    /// other state would make <c>draft</c> reachable to learners, which is the defect this method
    /// exists to close rather than to move.
    /// </para>
    /// <para>
    /// <b>Why the batch must be the item's own.</b> The decision names (batch, item) and ADM-02 reads
    /// the batch's uploader; clearing an item that arrived in some other batch would let a reviewer
    /// publish stock nobody sampled, under a decision whose batch says otherwise. The link is the
    /// verdict row the ingestion seam wrote, which is the only durable record of "this item came from
    /// that upload".
    /// </para>
    /// <para>
    /// <b>The permission is <c>pool.content.review</c>, at the route</b>; the invariant ADM-02 is
    /// enforced here and in the database: the row is a <see cref="ReviewSampleDecision"/> with the
    /// <c>clear</c> verb, and <c>trg_ReviewSampleDecisions_NoSelfApproval</c> refuses the batch's
    /// uploader or last modifier. A rule that lived only in a controller would be bypassed by the next
    /// endpoint someone writes (ADM-04). A triage-only account is refused by
    /// <c>trg_Items_TriageCannotWrite</c> (error 50081) on the item write itself, and holds no review
    /// namespace, so no route offers it either.
    /// </para>
    /// </summary>
    public async Task ClearForServiceAsync(
        int batchId,
        int itemId,
        int clearedBy,
        CancellationToken cancellationToken = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.ItemId == itemId, cancellationToken)
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Item {itemId} not found.", 404);

        if (!string.Equals(item.LifecycleStatus, "validated", StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Item {itemId} is '{item.LifecycleStatus}', not 'validated'. Only a staged item can be "
                + "cleared: a draft was never judged by the gates, and an active one is already live.", 409);
        }

        var arrivedInThisBatch = await db.ImportLogs
            .AsNoTracking()
            .AnyAsync(l => l.BatchId == batchId && l.ItemId == itemId, cancellationToken);

        if (!arrivedInThisBatch)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Item {itemId} did not arrive in batch {batchId}, so that batch's records do not cover "
                + "it. Clear it under the upload it came from.", 409);
        }

        item.LifecycleStatus = "active";

        db.ReviewSampleDecisions.Add(new ReviewSampleDecision
        {
            BatchId = batchId,
            ItemId = itemId,
            ActorUserId = clearedBy,
            Decision = "clear",
            DecidedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is { } inner
            && inner.Message.Contains("ADM-02", StringComparison.Ordinal))
        {
            // The refusal is the database's. Handing the caller a raw SQL sentence would make the rule
            // look like an accident, so it is restated as the rule.
            throw new AdminItemException(
                ApiError.Codes.Forbidden,
                "ADM-02: this account cannot clear a review sample for a batch or item it uploaded or "
                + "last modified, nor one whose newest version it wrote.", 403);
        }
    }

    // ---- reading -----------------------------------------------------------------------------

    /// <summary>The pool batches this workbench writes, newest first.</summary>
    public async Task<WorkbenchBatchList> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        var batches = db.ImportBatches.AsNoTracking()
            .Where(b => b.CertId == null)
            .OrderByDescending(b => b.BatchId)
            .Take(50);

        return new WorkbenchBatchList(await SummariesAsync(batches, cancellationToken));
    }

    /// <summary>
    /// One batch with <b>one row per record</b>, each at its highest attempt, plus the staged items
    /// those records became.
    /// <para>
    /// The grouping is the criterion: <c>RowNo</c> is the record's line, so a record re-judged twice is
    /// still one row, showing the latest judgement. A batch-level note has no <c>RowNo</c> and drops out
    /// of the same filter - which is what makes this a rule rather than a convention a page follows by
    /// accident.
    /// </para>
    /// <para>
    /// #207 AC1: <see cref="WorkbenchBatchDetail.RuleGroups"/> counts refusals per
    /// <c>RuleCode</c>, across every record's <b>latest</b> verdict, so "40 of 50 failures are one
    /// rule" is one number rather than fifty rows read by eye. Discarded records and accepted ones
    /// contribute nothing: a record that was withdrawn is not still failing a rule.
    /// </para>
    /// </summary>
    public async Task<WorkbenchBatchDetail> GetBatchAsync(
        int batchId,
        string? rule = null,
        CancellationToken cancellationToken = default)
    {
        var batches = db.ImportBatches.AsNoTracking().Where(b => b.BatchId == batchId);
        var batch = (await SummariesAsync(batches, cancellationToken)).SingleOrDefault()
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Import batch {batchId} not found.", 404);

        var rows = await db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batchId && l.RowNo != null)
            .ToListAsync(cancellationToken);

        // The winning attempt per record. ThenByDescending(LogId) makes the choice deterministic when
        // two rows claim one attempt, which only a hand-run fixture can produce.
        var latest = rows
            .GroupBy(l => l.RowNo!.Value)
            .Select(group => group.OrderByDescending(l => l.Attempt).ThenByDescending(l => l.LogId).First())
            .OrderBy(l => l.RowNo)
            .ToList();

        // A record's refusals are its error rows at the winning attempt, in write order: the first is
        // the rule gate one names first, and that is the page's headline.
        var refusals = rows
            .Where(l => l.Level == "error")
            .GroupBy(l => (Line: l.RowNo!.Value, l.Attempt))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IngestionRefusal>)g
                    .OrderBy(l => l.LogId)
                    .Select(l => new IngestionRefusal(l.RuleCode ?? string.Empty, l.Message))
                    .ToList());

        var source = string.IsNullOrWhiteSpace(batch.SourceName) ? null : await SourceDocumentAsync(batchId, cancellationToken);

        // #207 AC1: every record's latest-verdict refusals, counted by rule code. This is computed over
        // the whole batch before the `rule` filter below, so the counts a reviewer reads are the counts
        // of the batch and do not shrink to whatever they are currently looking at. Counted by record,
        // not by refusal row: "40 records break gate one" is the fact a reviewer acts on.
        var groups = latest
            .Where(row => !IsDiscard(row))
            .SelectMany(row => (refusals.GetValueOrDefault((row.RowNo!.Value, row.Attempt)) ?? [])
                .Select(refusal => (Line: row.RowNo!.Value, refusal.Rule)))
            .Where(entry => entry.Rule.Length > 0)
            .GroupBy(entry => entry.Rule, StringComparer.Ordinal)
            .Select(group => new WorkbenchRuleGroup(group.Key, group.Select(e => e.Line).Distinct().Count()))
            .OrderByDescending(group => group.Records)
            .ThenBy(group => group.Rule, StringComparer.Ordinal)
            .ToList();

        var records = latest
            .Where(row => rule is null
                || (refusals.GetValueOrDefault((row.RowNo!.Value, row.Attempt)) ?? [])
                    .Any(refusal => string.Equals(refusal.Rule, rule, StringComparison.Ordinal)))
            .Select(row =>
            {
                var discarded = IsDiscard(row);
                var accepted = string.Equals(row.Level, "info", StringComparison.Ordinal);
                return new WorkbenchRecordRow(
                    row.RowNo!.Value,
                    row.Attempt,
                    accepted,
                    row.ItemId,
                    accepted || discarded
                        ? []
                        : refusals.GetValueOrDefault((row.RowNo!.Value, row.Attempt)) ?? [],
                    source is null ? null : RecordTextAt(source, row.RowNo!.Value),
                    discarded,
                    discarded ? row.Message : null);
            })
            .ToList();

        var staged = await db.Items.AsNoTracking()
            .Where(i => db.ImportLogs.Any(l => l.BatchId == batchId && l.ItemId == i.ItemId))
            .OrderBy(i => i.ItemId)
            .Select(i => new WorkbenchStagedItem(
                i.ItemId,
                i.Topic.Code,
                i.CefrLevel.Code,
                i.Modality,
                i.LifecycleStatus,
                db.ItemVersions.Where(v => v.ItemId == i.ItemId).Max(v => v.VersionNumber)))
            .ToListAsync(cancellationToken);

        return new WorkbenchBatchDetail(batch, records, groups, staged);
    }

    /// <summary>
    /// #207 AC4: the batch's status, derived from its records rather than assigned by whichever write
    /// path remembered to set it.
    /// <para>
    /// <b>The rule, stated as an invariant rather than a transition.</b> A record is accepted,
    /// discarded, or still refused at its latest attempt. <c>done</c> means <b>no record is still
    /// refused</b> - every one is either accepted or withdrawn. <c>failed</c> means at least one is
    /// still refused. <c>pending</c> means nothing has been judged.
    /// </para>
    /// <para>
    /// <b>Why the rule is an invariant and not "re-run the upload's formula".</b> #205 assigned
    /// <c>done</c> unless <em>every</em> record was refused, so a failed file with one child corrected
    /// would read <c>done</c> while its sibling was still refused - the exact flip this ticket warns
    /// about. Deriving from the set makes that unrepresentable: no refusal, <c>done</c>; a refusal,
    /// not <c>done</c>.
    /// </para>
    /// <para>
    /// <b>A batch already <c>done</c> cannot gain a rejection.</b> A re-submission writes a verdict for
    /// a line that already exists and that was already refused (correcting an accepted line is not
    /// offered), and a discard only lowers the refusal count. The one write that adds a rejection is a
    /// fresh upload, which starts <c>pending</c>. If one were ever added anyway, this derivation would
    /// flip the batch back to <c>failed</c> rather than leave a stale <c>done</c>: the status is a
    /// function of the verdicts, never a latch.
    /// </para>
    /// </summary>
    private async Task RecomputeStatusAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        var rows = await db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batch.BatchId && l.RowNo != null)
            .ToListAsync(cancellationToken);

        var latest = rows
            .GroupBy(l => l.RowNo!.Value)
            .Select(group => group.OrderByDescending(l => l.Attempt).ThenByDescending(l => l.LogId).First())
            .ToList();

        batch.Status = latest.Count == 0
            ? "pending"
            : latest.Any(IsRefusal) ? "failed" : "done";
    }

    /// <summary>
    /// A record is still failing when its latest verdict is an error under a rule: a discard is a
    /// <c>warn</c> under <see cref="DiscardRule"/>, and an acceptance is an <c>info</c>.
    /// </summary>
    private static bool IsRefusal(ImportLog row) =>
        string.Equals(row.Level, "error", StringComparison.Ordinal) && !IsDiscard(row);

    /// <summary>A withdrawn record: the newest verdict says so, under the workbench's own rule code.</summary>
    private static bool IsDiscard(ImportLog row) =>
        string.Equals(row.RuleCode, DiscardRule, StringComparison.Ordinal);


    // ---- recording ---------------------------------------------------------------------------

    /// <summary>
    /// One row per record, plus one line-less note for the batch. A refused record gets one row per
    /// broken rule, all at the same line and attempt, so the page can show every reason without a
    /// second query - while the record count stays one per line.
    /// </summary>
    private async Task RecordVerdictsAsync(
        int batchId,
        IReadOnlyList<IngestionRecordResult> records,
        int attempt,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var record in records)
        {
            if (record.Accepted)
            {
                db.ImportLogs.Add(new ImportLog
                {
                    BatchId = batchId,
                    RowNo = record.Line,
                    Level = "info",
                    Message = Clip(
                        $"Accepted: {record.RecordId} passed every gate and is staged for review.", 1000),
                    RuleCode = null,
                    Attempt = attempt,
                    ItemId = record.ItemId,
                    LoggedAt = now,
                });
                continue;
            }

            foreach (var refusal in record.Refusals)
            {
                db.ImportLogs.Add(new ImportLog
                {
                    BatchId = batchId,
                    RowNo = record.Line,
                    Level = "error",
                    Message = Clip(refusal.Message, 1000),
                    RuleCode = Clip(refusal.Rule, 40),
                    Attempt = attempt,
                    ItemId = null,
                    LoggedAt = now,
                });
            }
        }

        // The batch-level note names no line, which is what keeps it out of the per-record list.
        db.ImportLogs.Add(new ImportLog
        {
            BatchId = batchId,
            RowNo = null,
            Level = records.Count > 0 && records.All(r => !r.Accepted) ? "error" : "info",
            Message = Clip(
                $"{records.Count(r => r.Accepted)} record(s) accepted, {records.Count(r => !r.Accepted)} refused.",
                1000),
            Attempt = attempt,
            LoggedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> NextAttemptAsync(int batchId, CancellationToken cancellationToken)
    {
        var highest = await db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batchId && l.RowNo != null)
            .MaxAsync(l => (int?)l.Attempt, cancellationToken);
        return (highest ?? 0) + 1;
    }

    /// <summary>
    /// The batch as the list and the detail both read it: <b>one</b> projection, because two would
    /// eventually report different counts for one batch.
    /// <para>
    /// <b>#207 fixes the two counts #205 got wrong.</b> Both were taken at <c>Attempt = 1</c>, which
    /// the comment called "the latest attempt" and is the opposite: a record refused at attempt 1 and
    /// corrected at attempt 2 was counted as refused forever, so a fully remediated batch still read
    /// "0 accepted". And <c>Records</c> counted rows at attempt 1, so a record that broke two rules
    /// contributed two rows to a "records" count. The count is now per <c>RowNo</c> at its highest
    /// attempt, which is the same definition the verdict list uses.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<WorkbenchBatchSummary>> SummariesAsync(
        IQueryable<ImportBatch> batches, CancellationToken cancellationToken)
    {
        var batchIds = await batches.Select(b => b.BatchId).ToListAsync(cancellationToken);
        if (batchIds.Count == 0)
        {
            return [];
        }

        var headers = await db.ImportBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.BatchId))
            .Select(b => new
            {
                b.BatchId,
                b.SourceName,
                b.Status,
                b.UploadedBy,
                b.UploadedByNavigation.Email,
                b.CreatedAt,
                b.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        // The latest verdict per record, in memory: the winner is per line, and a GROUP BY with a
        // window function over a second table is not something to make EF translate.
        var verdicts = await db.ImportLogs.AsNoTracking()
            .Where(l => batchIds.Contains(l.BatchId) && l.RowNo != null)
            .Select(l => new { l.BatchId, l.RowNo, l.Attempt, l.LogId, l.Level, l.RuleCode })
            .ToListAsync(cancellationToken);

        var latest = verdicts
            .GroupBy(v => (v.BatchId, v.RowNo))
            .Select(group => group.OrderByDescending(v => v.Attempt).ThenByDescending(v => v.LogId).First())
            .GroupBy(v => v.BatchId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return headers
            .OrderByDescending(h => h.BatchId)
            .Select(h =>
            {
                var records = latest.GetValueOrDefault(h.BatchId) ?? [];
                return new WorkbenchBatchSummary(
                    h.BatchId, h.SourceName, h.Status, h.UploadedBy, h.Email,
                    h.CreatedAt, h.CompletedAt,
                    records.Count,
                    records.Count(r => string.Equals(r.Level, "info", StringComparison.Ordinal)),
                    records.Count(r => string.Equals(r.RuleCode, DiscardRule, StringComparison.Ordinal)));
            })
            .ToList();
    }


    /// <summary>The record's own latest verdict, or null when the batch holds no such line.</summary>
    private Task<ImportLog?> LatestVerdictAsync(int batchId, int line, CancellationToken cancellationToken) =>
        db.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batchId && l.RowNo == line)
            .OrderByDescending(l => l.Attempt)
            .ThenByDescending(l => l.LogId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<string?> SourceDocumentAsync(int batchId, CancellationToken cancellationToken) =>
        db.ImportBatches.AsNoTracking()
            .Where(b => b.BatchId == batchId)
            .Select(b => b.SourceDocument)
            .SingleOrDefaultAsync(cancellationToken);

    // ---- the corrected document --------------------------------------------------------------

    /// <summary>
    /// The corrected record spliced into its original bundle, so the gates judge it with the passage
    /// and siblings the first judgement used - and the verdict still names the line the author finds
    /// it on, because the rebuilt document is read by the same reader the upload uses.
    /// <para>
    /// The ids the pool keys on are preserved: a correction may not rename the record or its stimulus,
    /// because that would stage a second item instead of fixing the refused one, and a renamed
    /// stimulus would silently detach the siblings that share it.
    /// </para>
    /// </summary>
    private static CorrectedBundle RebuildWithCorrectedRecord(
        string sourceDocument, int line, string correctedRecordJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(sourceDocument, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException exception)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"The batch's stored document can no longer be read: {exception.Message}", 409);
        }

        if (root is not JsonObject document || document["stimulus_groups"] is not JsonArray groups)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation, "The batch's stored document carries no stimulus_groups.", 409);
        }

        JsonObject corrected;
        try
        {
            corrected = JsonNode.Parse(correctedRecordJson, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject
                ?? throw new JsonException("the corrected record is not a JSON object");
        }
        catch (JsonException exception)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation, $"The corrected record is not valid JSON: {exception.Message}", 400);
        }

        // Which record the correction is for: the id the batch already holds, never one it invents.
        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g] is not JsonObject group || group["items"] is not JsonArray items)
            {
                continue;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject item)
                {
                    continue;
                }

                var storedId = (string?)item["item_id"];
                if (string.IsNullOrEmpty(storedId)
                    || !string.Equals(storedId, (string?)corrected["item_id"], StringComparison.Ordinal))
                {
                    continue;
                }

                items[i] = corrected;

                // One group, re-read on its own. The reader is the only definition of a record's line
                // in this codebase, so the corrected verdict names the same place as the original.
                //
                // WriteIndented is load-bearing, not cosmetic: the reader's line numbers are newline
                // counts, and the gates key their per-record verdicts by line. A compact group puts
                // every child on one line, so two children collide on one key and the second is
                // silently dropped - a resubmit that wrote one item where the bundle had two.
                return new CorrectedBundle(
                    $$"""
                    {
                      "schema_version": "{{(string?)document["schema_version"]}}",
                      "skill": "{{(string?)document["skill"]}}",
                      "stimulus_groups": [
                    {{group.ToJsonString(IndentedJson)}}
                      ],
                      "productive_tasks": []
                    }
                    """,
                    (string?)document["skill"],
                    storedId);
            }
        }

        throw new AdminItemException(
            ApiError.Codes.Validation,
            "The batch holds no record with the id the correction names. A correction fixes an existing "
            + "record; it does not add one.", 404);
    }

    /// <summary>
    /// The record at one line, rendered back for the correction form. Parsed rather than sliced so the
    /// text is valid JSON a person can edit; null when the line holds no record.
    /// </summary>
    private static string? RecordTextAt(string sourceDocument, int line)
    {
        try
        {
            var file = SkillContentPoolReader.Read(Encoding.UTF8.GetBytes(sourceDocument));
            foreach (var bundle in file.Bundles)
            {
                foreach (var record in bundle.Items)
                {
                    if (record.Line == line)
                    {
                        return record.RawJson;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string Clip(string value, int length) =>
        length > 0 && value.Length > length ? value[..length] : value;

    /// <summary>One group of the stored document, re-serialised so its records keep their original lines.</summary>
    private sealed record CorrectedBundle(string FileJson, string? Skill, string RecordId);
}
