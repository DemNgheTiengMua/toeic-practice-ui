using System;
using System.Collections.Generic;

namespace Contracts;

// Question batch import (ADR-0007): an admin uploads a manifest (.json) or a package (.zip with
// manifest.json and media/), the API validates it as a whole and either creates one draft exam or
// records why not. Every upload that names a real certificate leaves an ImportBatches row.

public sealed record AdminImportLogDto(
    int LogId,
    int? RowNo,
    ImportLogLevelDto Level,
    string Message,
    DateTimeOffset LoggedAt);

public sealed record AdminImportBatchListItemDto(
    int BatchId,
    int CertId,
    string CertCode,
    string SourceName,
    ImportBatchStatusDto Status,
    string UploadedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int ErrorCount,
    int WarningCount);

public sealed record AdminImportBatchesDto(
    IReadOnlyList<AdminImportBatchListItemDto> Batches,
    IReadOnlyList<AdminCertificateOptionDto> Certificates);

public sealed record AdminImportBatchDetailDto(
    AdminImportBatchListItemDto Batch,
    IReadOnlyList<AdminImportLogDto> Logs);

/// <summary>The outcome of one upload. <see cref="ExamId"/> is set only when the batch is done.</summary>
public sealed record AdminImportResultDto(
    int BatchId,
    ImportBatchStatusDto Status,
    int? ExamId,
    string? ExamName,
    int QuestionCount,
    int ErrorCount,
    int WarningCount);
