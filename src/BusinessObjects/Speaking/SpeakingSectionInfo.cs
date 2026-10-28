using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingSectionInfo(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? PrepSeconds,
    int? ResponseSeconds,
    int DisplayOrder
);
