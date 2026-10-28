using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record StartStepResult(
    int StepId,
    PathModuleItemType ItemType,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId);
