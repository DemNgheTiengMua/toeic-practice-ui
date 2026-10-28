using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SkillScoreScaleGroupDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int MaxQuestions,
    IReadOnlyList<ScoreScaleRowDto> Scales);
