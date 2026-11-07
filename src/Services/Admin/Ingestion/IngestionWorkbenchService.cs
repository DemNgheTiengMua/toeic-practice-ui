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
    // ---- uploading ---------------------------------------------------------------------------

    /// <summary>
    /// Accepts a file for one skill, records the batch and one verdict per record, and stages every
    /// accepted record as <c>validated</c>.
    /// <para>
    /// A partly valid file is the normal shape, not an error: the accepted records are written and the
    /// refused ones are listed. The batch's status says which happened, and a file whose every record
    /// was refused is <c>failed</c> rather than <c>done</c> - a distinction the author acts on.
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
        batch.Status = judged.Records.Count > 0 && judged.Records.All(r => !r.Accepted) ? "failed" : "done";
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

        await db.SaveChangesAsync(cancellationToken);

        log?.LogInformation(
            "Batch {BatchId} line {Line} re-judged by account {SubmittedBy} at attempt {Attempt}: {Outcome}.",
            batchId, line, submittedBy, attempt, verdict.Accepted ? "accepted" : "refused");

        return verdict;
    }

    // ---- the audit's clearance ---------------------------------------------------------------

    /// <summary>
    /// AC2's second half: the transition the staging criterion depends on.
    /// <para>
    /// <b>Why this had to exist.</b> Nothing in <c>src/</c> moved an item out of <c>validated</c>
    /// before this. A workbench that shows "staged" and offers no way to clear it is a page that parks
    /// content forever while staff believe it is queued - worse than no page, because it reports a
    /// queue that never drains.
    /// </para>
    /// <para>
    /// <b>The permission is <c>pool.content.review</c>, at the route</b>; the invariant ADM-02 is
    /// enforced here and in the database: the row is a <see cref="ReviewSampleDecision"/> with the
    /// <c>clear</c> verb, and <c>trg_ReviewSampleDecisions_NoSelfApproval</c> refuses the batch's
    /// uploader or last modifier. A rule that lived only in a controller would be bypassed by the next
    /// endpoint someone writes (ADM-04).
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
        var rows = await db.ImportBatches.AsNoTracking()
            .Where(b => b.CertId == null)
            .OrderByDescending(b => b.BatchId)
            .Take(50)
            .Select(b => new
            {
                b.BatchId,
                b.SourceName,
                b.Status,
                b.UploadedBy,
                b.UploadedByNavigation.Email,
                b.CreatedAt,
                b.CompletedAt,
                // Counted at the latest attempt so a re-submitted record is not counted twice.
                Records = b.ImportLogs.Count(l => l.RowNo != null && l.Attempt == 1),
                Accepted = b.ImportLogs.Count(l => l.RowNo != null && l.Attempt == 1 && l.Level == "info"),
            })
            .ToListAsync(cancellationToken);

        return new WorkbenchBatchList(rows
            .Select(r => new WorkbenchBatchSummary(
                r.BatchId, r.SourceName, r.Status, r.UploadedBy, r.Email,
                r.CreatedAt, r.CompletedAt, r.Records, r.Accepted))
            .ToList());
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
    /// </summary>
    public async Task<WorkbenchBatchDetail> GetBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.AsNoTracking()
            .Where(b => b.BatchId == batchId)
            .Select(b => new WorkbenchBatchSummary(
                b.BatchId, b.SourceName, b.Status, b.UploadedBy, b.UploadedByNavigation.Email,
                b.CreatedAt, b.CompletedAt,
                b.ImportLogs.Count(l => l.RowNo != null && l.Attempt == 1),
                b.ImportLogs.Count(l => l.RowNo != null && l.Attempt == 1 && l.Level == "info")))
            .SingleOrDefaultAsync(cancellationToken)
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
        var records = latest.Select(row =>
        {
            var accepted = string.Equals(row.Level, "info", StringComparison.Ordinal);
            return new WorkbenchRecordRow(
                row.RowNo!.Value,
                row.Attempt,
                accepted,
                row.ItemId,
                accepted
                    ? []
                    : refusals.GetValueOrDefault((row.RowNo!.Value, row.Attempt)) ?? [],
                source is null ? null : RecordTextAt(source, row.RowNo!.Value));
        }).ToList();

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

        return new WorkbenchBatchDetail(batch, records, staged);
    }

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
