using System;
using System.Collections.Generic;

namespace Contracts;

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
