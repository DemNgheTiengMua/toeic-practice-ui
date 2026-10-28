using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ModuleStepDto(
    int StepId,
    int ItemId,
    PathModuleItemTypeDto ItemType,
    string Title,
    LearnerPathStepStatusDto Status,
    decimal? Score,
    int? QuestionCount,
    bool IsPremiumOnly,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId,
    int? RefLessonId = null);
