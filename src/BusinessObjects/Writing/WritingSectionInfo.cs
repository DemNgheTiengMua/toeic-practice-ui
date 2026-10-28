using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingSectionInfo(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? ResponseSeconds,
    int DisplayOrder
);
