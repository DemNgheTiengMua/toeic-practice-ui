using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>
/// #205 / S-02: the ingestion and staging workbench's calls.
/// <para>
/// Separate from <see cref="IAdminImportsClient"/> because the two answer different questions: that
/// client imports a manifest into a draft exam, naming a certificate; this one ingests certificate-
/// agnostic pool stock and reports a verdict per record. A shared client would have to guess which
/// format it had been handed.
/// </para>
/// </summary>
public interface IAdminIngestionClient
{
    Task<IngestionBatchListDto> GetWorkbenchBatchesAsync(CancellationToken cancellationToken = default);
    Task<IngestionBatchListDto> GetWorkbenchBatchesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetWorkbenchBatchesAsync(cancellationToken);

    Task<IngestionBatchDetailDto> GetWorkbenchBatchAsync(int batchId, string? rule = null, CancellationToken cancellationToken = default);
    Task<IngestionBatchDetailDto> GetWorkbenchBatchAsync(int batchId, string? rule, string bearerToken, CancellationToken cancellationToken = default) =>
        GetWorkbenchBatchAsync(batchId, rule, cancellationToken);

    Task<IngestionUploadResultDto> UploadWorkbenchFileAsync(string skill, Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<IngestionUploadResultDto> UploadWorkbenchFileAsync(string skill, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default) =>
        UploadWorkbenchFileAsync(skill, content, fileName, cancellationToken);

    Task<IngestionRecordDto> ResubmitWorkbenchRecordAsync(int batchId, int line, string correctedRecordJson, CancellationToken cancellationToken = default);
    Task<IngestionRecordDto> ResubmitWorkbenchRecordAsync(int batchId, int line, string correctedRecordJson, string bearerToken, CancellationToken cancellationToken = default) =>
        ResubmitWorkbenchRecordAsync(batchId, line, correctedRecordJson, cancellationToken);

    /// <summary>#207 AC3: withdraw one refused record. The refusals that led to it stay readable.</summary>
    Task DiscardWorkbenchRecordAsync(int batchId, int line, string? reason, CancellationToken cancellationToken = default);
    Task DiscardWorkbenchRecordAsync(int batchId, int line, string? reason, string bearerToken, CancellationToken cancellationToken = default) =>
        DiscardWorkbenchRecordAsync(batchId, line, reason, cancellationToken);

    Task ClearWorkbenchItemAsync(int batchId, int itemId, CancellationToken cancellationToken = default);
    Task ClearWorkbenchItemAsync(int batchId, int itemId, string bearerToken, CancellationToken cancellationToken = default) =>
        ClearWorkbenchItemAsync(batchId, itemId, cancellationToken);
}
