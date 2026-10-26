using System.Collections.Generic;

namespace Contracts;

public sealed record ExamHistoryDto(
    CertificateOptionDto Certificate,
    int TotalAttempts,
    int? BestScore,
    int? AverageScore,
    string? BestScoreBandCode,
    string? BestScoreBandName,
    IReadOnlyList<ScoreBandItemDto> CefrBands,
    IReadOnlyList<ExamHistoryItemDto> Attempts);
