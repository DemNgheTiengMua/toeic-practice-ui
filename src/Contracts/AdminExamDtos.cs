using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamListItemDto(
    int ExamId,
    int CertId,
    string CertCode,
    string CertName,
    string Name,
    ExamStatusDto Status,
    ExamPurposeDto Purpose,
    int DurationMinutes,
    int? QuestionCount,
    int ActualQuestionCount,
    string AudioStatus,
    int FinishedAttemptCount,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset CreatedAt,
    string? SourceName = null);

public sealed record AdminCertificateOptionDto(
    int CertId,
    string Code,
    string Name);

public sealed record AdminExamListSummaryDto(
    IReadOnlyList<AdminExamListItemDto> Exams,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AdminCertificateOptionDto> Certificates);

public sealed record CreateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    ExamStatusDto Status = ExamStatusDto.Draft,
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);

public sealed record UpdateAdminExamRequest(
    int CertId,
    string Name,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    ExamStatusDto Status,
    string? Description = null,
    string? SourceName = null,
    string? InternalNote = null,
    int? QuestionCount = null);

public sealed record AdminExamSectionAudioDto(
    int ExamId,
    int SectionId,
    string SectionCode,
    string SectionName,
    string AudioPath,
    int DurationSec,
    long SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record AdminExamSectionProgressDto(
    int SectionId,
    string SectionCode,
    string SectionName,
    ModalityDto Modality,
    int QuestionCount,
    int? ExpectedQuestionCount,
    bool HasAudio,
    bool IsComplete,
    string StatusText);

public sealed record AdminExamDetailDto(
    int ExamId,
    int CertId,
    string CertCode,
    string CertName,
    string Name,
    ExamStatusDto Status,
    int DurationMinutes,
    ExamPurposeDto Purpose,
    string? Description,
    string? SourceName,
    string? InternalNote,
    int? QuestionCount,
    int ActualQuestionCount,
    int FinishedAttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AdminExamSectionAudioDto> Audios,
    IReadOnlyList<AdminExamSectionProgressDto> SectionProgress);

public sealed record AdminPublishExamResultDto(
    bool Success,
    string Message,
    IReadOnlyList<string> MissingItems);

public sealed record AdminQuestionOptionDto(
    int OptionId,
    int QuestionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);

public sealed record AdminQuestionOptionInput(
    int? OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);

public sealed record AdminQuestionDto(
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
    string? CategoryName,
    int? PrepSeconds,
    int? ResponseSeconds,
    int? MinWords,
    int? MaxWords,
    string? Hint,
    int DisplayOrder,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AdminQuestionOptionDto> Options,
    bool HasAnswers);

public sealed record AdminQuestionGroupDto(
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
    IReadOnlyList<AdminQuestionDto> Questions);

public sealed record AdminSectionOptionDto(
    int SectionId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? ExpectedQuestionCount,
    int DisplayOrder);

public sealed record AdminQuestionCategoryOptionDto(
    int CategoryId,
    string Code,
    string Name);

public sealed record AdminExamQuestionsDto(
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    IReadOnlyList<AdminQuestionGroupDto> Groups,
    IReadOnlyList<AdminSectionOptionDto> AvailableSections,
    IReadOnlyList<AdminQuestionCategoryOptionDto> AvailableCategories);

public sealed record CreateQuestionGroupRequest(
    int SectionId,
    string? Passage = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? ImagePath = null,
    string? ImageAlt = null,
    string? Transcript = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int DisplayOrder = 0);

public sealed record UpdateQuestionGroupRequest(
    int SectionId,
    string? Passage = null,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? ImagePath = null,
    string? ImageAlt = null,
    string? Transcript = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int DisplayOrder = 0);

public sealed record CreateQuestionRequest(
    int GroupId,
    string Stem,
    int DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation = null,
    int? CategoryId = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int? MinWords = null,
    int? MaxWords = null,
    string? Hint = null,
    int DisplayOrder = 0,
    IReadOnlyList<AdminQuestionOptionInput>? Options = null,
    IReadOnlyList<int>? CriterionIds = null);

public sealed record UpdateQuestionRequest(
    int? GroupId,
    string Stem,
    int DifficultyLevel,
    QuestionTypeDto QuestionType,
    string? Explanation = null,
    int? CategoryId = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    int? MinWords = null,
    int? MaxWords = null,
    string? Hint = null,
    int DisplayOrder = 0,
    IReadOnlyList<AdminQuestionOptionInput>? Options = null,
    IReadOnlyList<int>? CriterionIds = null);

public sealed record SaveExamSectionAudioRequest(
    int SectionId,
    string AudioPath,
    int DurationSec,
    long SizeBytes);
