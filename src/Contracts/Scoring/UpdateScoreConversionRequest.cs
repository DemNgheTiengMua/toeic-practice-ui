using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateScoreConversionRequest(
    int? ExamId,
    IReadOnlyList<UpdateScoreScaleEntryDto> Scales,
    IReadOnlyList<UpdateScoreBandEntryDto> Bands);
