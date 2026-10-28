using System;
using System.Collections.Generic;

namespace Contracts;

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
