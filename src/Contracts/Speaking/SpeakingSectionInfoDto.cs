using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingSectionInfoDto(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? PrepSeconds,
    int? ResponseSeconds,
    int DisplayOrder
);

