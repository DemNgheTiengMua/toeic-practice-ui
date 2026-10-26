using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ExamHistoryView(
    CertificateOption Certificate,
    int TotalAttempts,
    int? BestScore,
    int? AverageScore,
    string? BestScoreBandCode,
    string? BestScoreBandName,
    IReadOnlyList<ScoreBandItem> CefrBands,
    IReadOnlyList<ExamHistoryItem> Attempts);
