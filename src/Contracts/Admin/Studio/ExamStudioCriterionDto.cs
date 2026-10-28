using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamStudioCriterionDto(
    int CriterionId,
    string Code,
    string Name,
    string? Description,
    decimal MaxScore,
    int SkillId = 0);
