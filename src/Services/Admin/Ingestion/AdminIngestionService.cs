using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

/// <summary>
/// #205: the wire shape of the staging workbench. A thin adapter over
/// <see cref="IngestionWorkbenchService"/>, which owns every decision; this exists so the endpoints
/// and the MVC client speak DTOs rather than domain records, and so mapping happens in one place.
/// </summary>
public interface IAdminIngestionService
{
    Task<IngestionBatchListDto> GetBatchesAsync(CancellationToken cancellationToken = default);

    Task<IngestionBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);

    Task<IngestionUploadResultDto> UploadAsync(
        string skill, Stream content, string fileName, int uploadedBy, CancellationToken cancellationToken = default);

    Task<IngestionRecordDto> ResubmitAsync(
        int batchId, int line, string correctedRecordJson, int submittedBy, CancellationToken cancellationToken = default);

    Task ClearAsync(int batchId, int itemId, int clearedBy, CancellationToken cancellationToken = default);
}

/// <summary>#205: the adapter itself. No rule lives here; every one is the service's.</summary>
public sealed class AdminIngestionService(IngestionWorkbenchService workbench) : IAdminIngestionService
{
    public async Task<IngestionBatchListDto> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        var list = await workbench.GetBatchesAsync(cancellationToken);
        var batches = new List<IngestionBatchSummaryDto>(list.Batches.Count);
        foreach (var batch in list.Batches)
        {
            batches.Add(ToDto(batch));
        }

        return new IngestionBatchListDto(batches);
    }

    public async Task<IngestionBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var detail = await workbench.GetBatchAsync(batchId, cancellationToken);

        var records = new List<IngestionRecordDto>(detail.Records.Count);
        foreach (var row in detail.Records)
        {
            var refusals = new List<IngestionRefusalDto>(row.Refusals.Count);
            foreach (var refusal in row.Refusals)
            {
                refusals.Add(new IngestionRefusalDto(refusal.Rule, refusal.Message));
            }

            records.Add(new IngestionRecordDto(
                row.Line, row.Attempt, row.Accepted, row.ItemId, refusals, row.RecordJson));
        }

        var staged = new List<IngestionStagedItemDto>(detail.Staged.Count);
        foreach (var item in detail.Staged)
        {
            staged.Add(new IngestionStagedItemDto(
                item.ItemId, item.TopicCode, item.CefrLevel, item.Modality, item.LifecycleStatus, item.VersionNumber));
        }

        return new IngestionBatchDetailDto(ToDto(detail.Batch), records, staged);
    }

    public async Task<IngestionUploadResultDto> UploadAsync(
        string skill, Stream content, string fileName, int uploadedBy, CancellationToken cancellationToken = default)
    {
        var result = await workbench.UploadAsync(skill, content, fileName, uploadedBy, cancellationToken);
        return new IngestionUploadResultDto(
            result.BatchId,
            result.SourceName,
            result.Skill,
            result.Status,
            result.Accepted.Count,
            result.Rejected.Count,
            result.ItemsWritten);
    }

    public async Task<IngestionRecordDto> ResubmitAsync(
        int batchId, int line, string correctedRecordJson, int submittedBy, CancellationToken cancellationToken = default)
    {
        var verdict = await workbench.ResubmitAsync(
            batchId, line, correctedRecordJson, submittedBy, cancellationToken);

        var refusals = new List<IngestionRefusalDto>(verdict.Refusals.Count);
        foreach (var refusal in verdict.Refusals)
        {
            refusals.Add(new IngestionRefusalDto(refusal.Rule, refusal.Message));
        }

        return new IngestionRecordDto(
            verdict.Line, Attempt: 0, verdict.Accepted, verdict.ItemId, refusals, RecordJson: null);
    }

    public Task ClearAsync(int batchId, int itemId, int clearedBy, CancellationToken cancellationToken = default) =>
        workbench.ClearForServiceAsync(batchId, itemId, clearedBy, cancellationToken);

    private static IngestionBatchSummaryDto ToDto(WorkbenchBatchSummary batch) => new(
        batch.BatchId,
        batch.SourceName,
        batch.Status,
        batch.UploadedBy,
        batch.UploadedByEmail,
        batch.CreatedAt,
        batch.CompletedAt,
        batch.Records,
        batch.Accepted);
}
