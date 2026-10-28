using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminSectionOptionDto(
    int SectionId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? ExpectedQuestionCount,
    int DisplayOrder);
