using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record StartStepResultDto(
    int StepId,
    PathModuleItemTypeDto ItemType,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId);
