using System;
using System.Collections.Generic;

namespace Contracts;

// --- Quick Ingest DTOs ---

public sealed record QuickIngestParseRequest(
    string RawText,
    int? SectionId = null,
    int? CertId = null);

public sealed record QuickIngestParsedOptionDto(
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale = null);

public sealed record QuickIngestIssueDto(
    int LineNumber,
    string Severity, // "error" | "warning"
    string Message,
    int? QuestionIndex = null);

public sealed record QuickIngestParsedQuestionDto(
    int Index,
    int StartLineNumber,
    int EndLineNumber,
    string Stem,
    int DifficultyLevel,
    string? Explanation,
    string? CategoryCode,
    IReadOnlyList<QuickIngestParsedOptionDto> Options,
    IReadOnlyList<QuickIngestIssueDto> Issues,
    bool IsValid);

public sealed record QuickIngestParseResultDto(
    bool Success,
    int TotalQuestions,
    int ValidQuestions,
    int ErrorCount,
    IReadOnlyList<QuickIngestParsedQuestionDto> Questions,
    IReadOnlyList<QuickIngestIssueDto> GlobalIssues);

public sealed record QuickIngestCommitRequest(
    int SectionId,
    string Mode, // "append" | "replace"
    IReadOnlyList<QuickIngestParsedQuestionDto> Questions);

public sealed record QuickIngestCommitResultDto(
    bool Success,
    int QuestionsCommitted,
    int GroupsCreated,
    string Message);

// --- Reorder and Audit DTOs ---

public sealed record ReorderSectionItemEntry(
    int? GroupId,
    int? QuestionId,
    int DisplayOrder);

public sealed record ReorderSectionItemsRequest(
    int SectionId,
    IReadOnlyList<ReorderSectionItemEntry> Items);

public sealed record ExamAuditIssueDto(
    string Code,
    string Severity, // "error" | "warning"
    string Message,
    int? SectionId = null,
    string? SectionCode = null,
    int? GroupId = null,
    int? QuestionId = null,
    string? ElementTarget = null);

public sealed record ExamAuditResultDto(
    bool CanPublish,
    int TotalQuestions,
    int ExpectedQuestions,
    int BlockingErrorCount,
    int WarningCount,
    IReadOnlyList<ExamAuditIssueDto> Issues,
    IReadOnlyList<string> MissingItems);

// --- Studio State DTOs ---

public sealed record ExamStudioQuestionOptionDto(
    int OptionId,
    int QuestionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);

public sealed record ExamStudioCriterionDto(
    int CriterionId,
    string Code,
    string Name,
    string? Description,
    decimal MaxScore);

public sealed record ExamStudioQuestionDto(
    int QuestionId,
    int GroupId,
    int ExamId,
    int SectionId,
    string SectionCode,
    string SectionName,
    int SkillId,
    string SkillCode,
    string Stem,
    int DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation,
    int? CategoryId,
    string? CategoryCode,
    string? CategoryName,
    int? PrepSeconds,
    int? ResponseSeconds,
    int? MinWords,
    int? MaxWords,
    string? Hint,
    int DisplayOrder,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<ExamStudioQuestionOptionDto> Options,
    IReadOnlyList<ExamStudioCriterionDto> Criteria,
    bool HasAnswers);

public sealed record ExamStudioGroupDto(
    int GroupId,
    int ExamId,
    int CertId,
    int SectionId,
    string SectionCode,
    string SectionName,
    ModalityDto Modality,
    string? Passage,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? ImagePath,
    string? ImageAlt,
    string? Transcript,
    int? PrepSeconds,
    int? ResponseSeconds,
    int DisplayOrder,
    IReadOnlyList<ExamStudioQuestionDto> Questions);

public sealed record ExamStudioSectionDto(
    int SectionId,
    int CertId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? ExpectedQuestionCount,
    int DisplayOrder,
    int CurrentQuestionCount,
    bool HasAudio,
    string? MasterAudioPath,
    int? MasterAudioDurationSec,
    IReadOnlyList<ExamStudioGroupDto> Groups);

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
