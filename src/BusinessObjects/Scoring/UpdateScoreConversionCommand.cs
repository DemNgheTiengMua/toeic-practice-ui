using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record UpdateScoreConversionCommand(
    int? ExamId,
    IReadOnlyList<UpdateScoreScaleEntry> Scales,
    IReadOnlyList<UpdateScoreBandEntry> Bands);
