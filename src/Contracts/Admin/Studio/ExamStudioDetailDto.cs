using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamStudioDetailDto(
    int ExamId,
    int CertId,
    string CertCode,
    string CertName,
    string Name,
    ExamStatusDto Status,
    ExamPurposeDto Purpose,
    int DurationMinutes,
    string? Description,
    string? SourceName,
    string? InternalNote,
    int? QuestionCount,
    int ActualQuestionCount,
    int FinishedAttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<ExamStudioSectionDto> Sections,
    IReadOnlyList<AdminQuestionCategoryOptionDto> AvailableCategories,
    IReadOnlyList<ExamStudioCriterionDto> AvailableCriteria,
    ExamAuditResultDto Audit);
