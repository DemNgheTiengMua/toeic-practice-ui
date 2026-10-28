using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SkillScoreScaleGroup(
    int SkillId,
    string SkillCode,
    string SkillName,
    int MaxQuestions,
    IReadOnlyList<ScoreScaleRowItem> Scales);
