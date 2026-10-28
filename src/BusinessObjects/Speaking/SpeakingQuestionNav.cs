using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingQuestionNav(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);
