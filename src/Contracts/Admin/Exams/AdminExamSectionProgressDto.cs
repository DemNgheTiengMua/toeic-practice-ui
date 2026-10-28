using System;
using System.Collections.Generic;

namespace Contracts;

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
