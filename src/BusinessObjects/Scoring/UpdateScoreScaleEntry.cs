using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record UpdateScoreScaleEntry(
    int SkillId,
    int RawScore,
    int ScaledScore);
