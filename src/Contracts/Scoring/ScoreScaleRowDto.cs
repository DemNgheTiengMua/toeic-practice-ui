using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ScoreScaleRowDto(
    int? ScaleId,
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int ScaledScore);
