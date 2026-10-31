using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IScoreConversionService
{
    Task<AdminScoreConversionView> GetScoreConversionAsync(string? certCode, int? examId = null, CancellationToken cancellationToken = default);

    Task<AdminScoreConversionView> UpdateScoreConversionAsync(int certId, UpdateScoreConversionCommand command, CancellationToken cancellationToken = default);

    Task<ScorePreviewResult> CalculateScorePreviewAsync(ScorePreviewRequest request, CancellationToken cancellationToken = default);

    ScorePreviewResult CalculateScorePreview(
        ScorePreviewRequest request,
        IReadOnlyList<ScoreScaleRowItem>? scales = null,
        IReadOnlyList<ScoreBandConfigItem>? bands = null,
        IReadOnlyDictionary<int, (int Min, int Max, int Step)>? skillRanges = null);
}
