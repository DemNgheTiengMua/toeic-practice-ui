using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateScoreScaleEntryDto(
    int SkillId,
    int RawScore,
    int ScaledScore);
