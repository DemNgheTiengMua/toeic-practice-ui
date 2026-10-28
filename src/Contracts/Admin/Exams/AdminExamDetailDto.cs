using System;
using System.Collections.Generic;

namespace Contracts;

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
