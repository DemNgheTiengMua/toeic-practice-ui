using System;
using System.Collections.Generic;

namespace Services;

/// <summary>
/// #205 / S-02: what one upload produced, as the workbench reads it back.
/// <para>
/// <see cref="BatchId"/> is the handle a reviewer uses to find the upload later; <see cref="UploadedBy"/>
/// and <see cref="CreatedAt"/> are the two facts criterion 4 asks for and the reason this is not the
/// bare <see cref="SkillContentPoolIngestionResult"/>.
/// </para>
/// </summary>
public sealed record WorkbenchBatchResult(
    int BatchId,
    string SourceName,
    string Skill,
    string Status,
    int UploadedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IngestionRecordResult> Records,
    int ItemsWritten,
    int VersionsWritten)
{
    public IReadOnlyList<IngestionRecordResult> Accepted => Filter(accepted: true);

    public IReadOnlyList<IngestionRecordResult> Rejected => Filter(accepted: false);

    private IReadOnlyList<IngestionRecordResult> Filter(bool accepted)
    {
        var matched = new List<IngestionRecordResult>();
        foreach (var record in Records)
        {
            if (record.Accepted == accepted)
            {
                matched.Add(record);
            }
        }

        return matched;
    }
}

/// <summary>#205: one batch, as a list shows it. <see cref="Accepted"/> is how many of its records passed.</summary>
public sealed record WorkbenchBatchSummary(
    int BatchId,
    string SourceName,
    string Status,
    int UploadedBy,
    string UploadedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int Records,
    int Accepted)
{
    public int Rejected => Records - Accepted;
}

public sealed record WorkbenchBatchList(IReadOnlyList<WorkbenchBatchSummary> Batches);

/// <summary>
/// #205: one record's row. <see cref="Line"/> is the file's own line; <see cref="Refusals"/> is every
/// rule that failed, in the order the gates reported them, so the first is the headline.
/// <para>
/// <see cref="ItemId"/> is the staged row an accepted record became. <see cref="RecordJson"/> is the
/// record as uploaded, so the page can offer it back for correction - a correction form with an empty
/// box would be a listing with a textarea under it.
/// </para>
/// </summary>
public sealed record WorkbenchRecordRow(
    int Line,
    int Attempt,
    bool Accepted,
    int? ItemId,
    IReadOnlyList<IngestionRefusal> Refusals,
    string? RecordJson)
{
    public string Message => Refusals.Count == 0 ? string.Empty : Refusals[0].Message;

    public string? Rule => Refusals.Count == 0 ? null : Refusals[0].Rule;
}

/// <summary>#205: an item the batch staged, and the state it is actually in.</summary>
public sealed record WorkbenchStagedItem(
    int ItemId,
    string TopicCode,
    string CefrLevel,
    string Modality,
    string LifecycleStatus,
    int VersionNumber);

/// <summary>
/// #205: one batch with <b>one row per record</b> and the items those records became.
/// <para>
/// <see cref="Records"/> is keyed by line, not by verdict: a record re-judged twice appears once, at
/// its latest attempt. That is what makes "one row per record" checkable rather than a page's private
/// convention.
/// </para>
/// </summary>
public sealed record WorkbenchBatchDetail(
    WorkbenchBatchSummary Batch,
    IReadOnlyList<WorkbenchRecordRow> Records,
    IReadOnlyList<WorkbenchStagedItem> Staged)
{
    /// <summary>
    /// The staged items that have <b>not</b> been cleared to active. This is the list the page labels
    /// "staged", and it is derived from the stored state rather than assumed: an item that reached
    /// active through any path drops out of it.
    /// </summary>
    public IReadOnlyList<WorkbenchStagedItem> AwaitingAudit
    {
        get
        {
            var waiting = new List<WorkbenchStagedItem>();
            foreach (var item in Staged)
            {
                if (string.Equals(item.LifecycleStatus, "validated", StringComparison.Ordinal))
                {
                    waiting.Add(item);
                }
            }

            return waiting;
        }
    }
}
