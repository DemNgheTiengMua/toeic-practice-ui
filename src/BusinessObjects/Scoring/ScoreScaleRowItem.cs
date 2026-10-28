using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ScoreScaleRowItem(
    int? ScaleId,
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int ScaledScore);
