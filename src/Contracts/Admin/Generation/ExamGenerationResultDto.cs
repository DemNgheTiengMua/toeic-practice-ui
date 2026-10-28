using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// Result of generating and ingesting an exam package.
/// </summary>
public sealed record ExamGenerationResultDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string Status,
    string Purpose,
    int QuestionCount,
    int? BatchId = null,
    string? RawManifestJson = null);
