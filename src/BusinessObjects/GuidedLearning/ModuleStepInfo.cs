using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ModuleStepInfo(
    int StepId,
    int ItemId,
    PathModuleItemType ItemType,
    string Title,
    LearnerPathStepStatus Status,
    decimal? Score,
    int? QuestionCount,
    bool IsPremiumOnly,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId,
    int? RefLessonId = null);
