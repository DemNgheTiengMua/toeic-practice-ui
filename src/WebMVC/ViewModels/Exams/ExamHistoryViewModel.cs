using Contracts;

namespace WebMVC.Models;

public sealed record ExamHistoryViewModel(ExamHistoryDto History, string? ErrorMessage = null)
{
    public IReadOnlyList<ExamHistoryAttemptViewModel> Attempts { get; init; } = [];
    public IReadOnlyList<ExamHistoryChartPointViewModel> ChartPoints { get; init; } = [];
    public IReadOnlyList<AttemptBandViewModel> Bands { get; init; } = [];
}
