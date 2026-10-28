using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingQuestionNavDto(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);

