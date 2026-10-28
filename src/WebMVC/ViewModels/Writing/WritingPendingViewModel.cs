using Contracts;

namespace WebMVC.Models;

public sealed record WritingPendingViewModel(
    WritingGradingPendingDto Pending,
    string? ErrorMessage = null)
{
    public double ProgressPercent { get; } = Math.Max(20,
        Pending.TotalResponses > 0 ? Math.Round((double)Pending.GradedResponses / Pending.TotalResponses * 100) : 50);
}
