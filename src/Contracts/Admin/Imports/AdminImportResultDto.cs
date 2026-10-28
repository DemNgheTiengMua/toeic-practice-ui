using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>The outcome of one upload. <see cref="ExamId"/> is set only when the batch is done.</summary>
public sealed record AdminImportResultDto(
    int BatchId,
    ImportBatchStatusDto Status,
    int? ExamId,
    string? ExamName,
    int QuestionCount,
    int ErrorCount,
    int WarningCount);
