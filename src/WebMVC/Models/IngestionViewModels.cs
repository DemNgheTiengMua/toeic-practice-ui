using System;
using System.Collections.Generic;
using System.Globalization;
using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #205 / S-02: the batch list. <see cref="UploadedBy"/> and <see cref="CreatedAt"/> are criterion 4
/// on the screen - who submitted what, and when.
/// </summary>
public sealed record IngestionBatchRow(
    int BatchId,
    string SourceName,
    string StatusLabel,
    bool AllRefused,
    string UploadedBy,
    string CreatedAt,
    string Records,
    string Accepted,
    string Discarded);

public sealed record IngestionBatchListViewModel(
    IReadOnlyList<IngestionBatchRow> Batches,
    string? ErrorMessage,
    string? SuccessMessage);

/// <summary>
/// #205: one record's row in the verdict list. Tabular, because a verdict list is tabular data: the
/// line and the counts are mono, and each rule is a machine code beside its sentence.
/// <para>
/// <see cref="Refusals"/> is every rule that failed, not only the first. A row that showed one reason
/// would send the author back for a second upload to discover the next one - and the gates already
/// report them all in one pass, which is what makes a single correction worth doing.
/// </para>
/// <para>
/// #207: <see cref="Discarded"/> is a third outcome beside accepted and refused, and
/// <see cref="Note"/> is the withdrawal sentence. A withdrawn record keeps its line number so the
/// reviewer can still find it in the file.
/// </para>
/// </summary>
public sealed record IngestionRecordRow(
    int Line,
    int Attempt,
    bool Accepted,
    IReadOnlyList<IngestionRefusalRow> Refusals,
    string? RecordJson,
    int? ItemId,
    bool Discarded = false,
    string? Note = null);

/// <summary>#207: one rule and the number of records still breaking it, with each line it holds.</summary>
public sealed record IngestionRuleGroupRow(
    string Rule,
    int Records,
    IReadOnlyList<int> Lines,
    bool IsFiltered);

/// <summary>#205: one broken rule, as a row shows it.</summary>
public sealed record IngestionRefusalRow(string Rule, string Message);

/// <summary>
/// #205: a staged item, and whether it is still awaiting the audit. <see cref="IsStaged"/> is derived
/// from the stored state, not assumed: an item clears out of this list when it reaches active.
/// </summary>
public sealed record IngestionStagedRow(
    int ItemId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    string LifecycleStatus,
    string StateLabel,
    bool IsStaged,
    int VersionNumber);

/// <summary>
/// #205: one batch's detail. <see cref="CanClear"/> is the reviewer's capability, so the clearance
/// control is absent for an author rather than present and refused.
/// </summary>
public sealed record IngestionBatchDetailViewModel(
    int BatchId,
    string SourceName,
    string StatusLabel,
    string StatusCode,
    string UploadedBy,
    string CreatedAt,
    string? CompletedAt,
    IReadOnlyList<IngestionRuleGroupRow> RuleGroups,
    IReadOnlyList<IngestionRecordRow> Records,
    IReadOnlyList<IngestionStagedRow> Staged,
    int AcceptedCount,
    int RejectedCount,
    int DiscardedCount,
    bool CanClear,
    string? SelectedRule,
    string? Message,
    string? ErrorMessage);

/// <summary>
/// #205: DTO to view mapping, in one place. Nothing here decides a verdict or a state - it turns a
/// code into a label and formats a date, because two definitions of "staged" would eventually
/// disagree about a row.
/// </summary>
public static class IngestionPresentation
{
    public static IngestionBatchListViewModel FromBatches(
        IngestionBatchListDto dto, string? message, string? error)
    {
        var rows = new List<IngestionBatchRow>(dto.Batches.Count);
        foreach (var batch in dto.Batches)
        {
            rows.Add(FromBatch(batch));
        }

        return new IngestionBatchListViewModel(rows, error, message);
    }

    public static IngestionBatchDetailViewModel FromDetail(
        IngestionBatchDetailDto dto, bool canClear, string? selectedRule, string? message, string? error)
    {
        var records = new List<IngestionRecordRow>(dto.Records.Count);
        foreach (var record in dto.Records)
        {
            var refusals = new List<IngestionRefusalRow>(record.Refusals.Count);
            foreach (var refusal in record.Refusals)
            {
                refusals.Add(new IngestionRefusalRow(refusal.Rule, refusal.Message));
            }

            records.Add(new IngestionRecordRow(
                record.Line,
                record.Attempt,
                record.Accepted,
                refusals,
                record.RecordJson,
                record.ItemId,
                record.Discarded,
                record.Note));
        }

        // #207 AC1: the lines each rule holds come from the rows on screen. The count itself is the
        // service's, computed over the whole batch, so a group does not report zero records merely
        // because the page is filtered to another rule.
        var groups = new List<IngestionRuleGroupRow>(dto.RuleGroups.Count);
        foreach (var group in dto.RuleGroups)
        {
            var lines = new List<int>();
            foreach (var record in dto.Records)
            {
                foreach (var refusal in record.Refusals)
                {
                    if (string.Equals(refusal.Rule, group.Rule, StringComparison.Ordinal))
                    {
                        lines.Add(record.Line);
                        break;
                    }
                }
            }

            groups.Add(new IngestionRuleGroupRow(
                group.Rule,
                group.Records,
                lines,
                string.Equals(group.Rule, selectedRule, StringComparison.Ordinal)));
        }

        var staged = new List<IngestionStagedRow>(dto.Staged.Count);
        foreach (var item in dto.Staged)
        {
            staged.Add(new IngestionStagedRow(
                item.ItemId,
                item.TopicCode,
                item.CefrLevel,
                item.Modality,
                item.LifecycleStatus,
                LifecycleLabel(item.LifecycleStatus),
                string.Equals(item.LifecycleStatus, "validated", StringComparison.Ordinal),
                item.VersionNumber));
        }

        var batch = FromBatch(dto.Batch);

        return new IngestionBatchDetailViewModel(
            batch.BatchId,
            batch.SourceName,
            batch.StatusLabel,
            dto.Batch.Status,
            batch.UploadedBy,
            batch.CreatedAt,
            dto.Batch.CompletedAt is { } completed
                ? completed.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                : null,
            groups,
            records,
            staged,
            dto.Batch.Accepted,
            dto.Batch.Records - dto.Batch.Accepted - dto.Batch.Discarded,
            dto.Batch.Discarded,
            canClear,
            selectedRule,
            message,
            error);
    }

    private static IngestionBatchRow FromBatch(IngestionBatchSummaryDto batch) => new(
        batch.BatchId,
        batch.SourceName,
        StatusLabel(batch.Status, batch.Accepted),
        // #207: "nothing accepted" is now a narrower fact than `failed`. A batch that landed some
        // records and still holds refusals is failed but not empty, and the two must not share a word.
        batch.Accepted == 0 && batch.Status == "failed",
        batch.UploadedByEmail,
        batch.CreatedAt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        batch.Records.ToString(CultureInfo.InvariantCulture),
        batch.Accepted.ToString(CultureInfo.InvariantCulture),
        batch.Discarded.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The batch's three states, in words an author acts on. A file whose every record was refused is
    /// not "done": it produced nothing, and saying otherwise is the sort of summary that hides a
    /// problem.
    /// <para>
    /// #207: <c>done</c> now means "no record is still refused", so a batch that was <c>failed</c> and
    /// has had every rejection corrected or withdrawn reads "All records settled" rather than
    /// "Nothing accepted". <c>failed</c> splits in two, because the two cases need different actions:
    /// nothing landed ("Nothing accepted") versus some landed and refusals remain ("Refusals remain").
    /// </para>
    /// </summary>
    private static string StatusLabel(string status, int accepted) => status switch
    {
        "pending" => "In progress",
        "failed" => accepted == 0 ? "Nothing accepted" : "Refusals remain",
        "done" => "All records settled",
        _ => status,
    };

    /// <summary>
    /// The six CNT-42 states in words. <c>validated</c> is deliberately "Staged, awaiting review" and
    /// not "live": a page that called it live would report content as servable that selection excludes.
    /// </summary>
    private static string LifecycleLabel(string status) => status switch
    {
        "draft" => "Draft, not judged",
        "validated" => "Staged, awaiting review",
        "active" => "In service",
        "quarantined" => "Quarantined",
        "retired" => "Retired",
        "retired_defective" => "Retired, defective",
        _ => status,
    };
}
