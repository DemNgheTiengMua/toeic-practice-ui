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
    bool IsValid,
    string QuestionType = "mcq",
    string? Passage = null,
    string? Transcript = null,
    int? MaxWords = null,
    int? MaxSelections = null,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    string? Hint = null,
    int? GroupNumber = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int? MinWords = null,
    bool AllowNumber = true);

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

public sealed record QuickIngestSampleTemplateDto(
    string Template,
    string FormatDescription);

public sealed record SectionGenerateRequest(
    int? QuestionCount = null);

public sealed record SectionGenerationResultDto(
    int ExamId,
    int SectionId,
    int QuestionsGenerated,
    int GroupsGenerated,
    string Message);

// --- Reorder and Audit DTOs ---

public sealed record ReorderSectionItemEntry(
    int? GroupId,
    int? QuestionId,
    int DisplayOrder);

public sealed record ReorderSectionItemsRequest(
    int SectionId,
    IReadOnlyList<ReorderSectionItemEntry> Items);

public sealed record CreateExamClusterRequest(
    int SectionId,
    string? Passage = null,
    string? Transcript = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    int QuestionCount = 3,
    int UnitNumber = 1);

public sealed record AdminExamMediaUploadResultDto(
    string Url,
    string Path,
    string FileName);

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
    decimal MaxScore,
    int SkillId = 0);

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
    bool HasAnswers,
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null);

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
    IReadOnlyList<ExamStudioQuestionDto> Questions,
    int UnitNumber = 1);

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
    IReadOnlyList<ExamStudioGroupDto> Groups,
    int OptionCount = 4,
    int ClusterSize = 1,
    int UnitCount = 1,
    bool HideOptionText = false,
    bool RequirePassage = false,
    bool RequireAudio = false,
    bool RequireImage = false,
    int RequiredKeywordCount = 0,
    string? AllowedComponentTypesJson = null,
    string LayoutType = "STANDARD_VERTICAL",
    string NavigationMode = "FULL_SANDBOX",
    string? TimerPolicyJson = null,
    string? ToolsConfigJson = null,
    int SkillId = 0,
    string? SkillCode = null,
    string? SkillName = null);

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
