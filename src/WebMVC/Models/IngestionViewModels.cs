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
    string Accepted);

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
/// </summary>
public sealed record IngestionRecordRow(
    int Line,
    int Attempt,
    bool Accepted,
    IReadOnlyList<IngestionRefusalRow> Refusals,
    string? RecordJson,
    int? ItemId);

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
    string UploadedBy,
    string CreatedAt,
    string? CompletedAt,
    IReadOnlyList<IngestionRecordRow> Records,
    IReadOnlyList<IngestionStagedRow> Staged,
    int AcceptedCount,
    int RejectedCount,
    bool CanClear,
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
        IngestionBatchDetailDto dto, bool canClear, string? message, string? error)
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
                record.ItemId));
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
            batch.UploadedBy,
            batch.CreatedAt,
            dto.Batch.CompletedAt is { } completed
                ? completed.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                : null,
            records,
            staged,
            dto.Batch.Accepted,
            dto.Batch.Records - dto.Batch.Accepted,
            canClear,
            message,
            error);
    }

    private static IngestionBatchRow FromBatch(IngestionBatchSummaryDto batch) => new(
        batch.BatchId,
        batch.SourceName,
        StatusLabel(batch.Status),
        string.Equals(batch.Status, "failed", StringComparison.Ordinal),
        batch.UploadedByEmail,
        batch.CreatedAt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        batch.Records.ToString(CultureInfo.InvariantCulture),
        batch.Accepted.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The batch's three states, in words an author acts on. A file whose every record was refused is
    /// not "done": it produced nothing, and saying otherwise is the sort of summary that hides a
    /// problem.
    /// </summary>
    private static string StatusLabel(string status) => status switch
    {
        "pending" => "In progress",
        "failed" => "Nothing accepted",
        "done" => "Judged",
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
