using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingSectionInfoDto(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? ResponseSeconds,
    int DisplayOrder
);

