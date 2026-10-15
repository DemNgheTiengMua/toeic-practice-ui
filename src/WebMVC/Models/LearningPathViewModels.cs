using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathIndexViewModel(
    LearningPathSummaryDto Summary,
    string? ErrorMessage = null,
    AiInsightPanelViewModel? InsightPanel = null,
    string? SuccessMessage = null);

public sealed record LearningPathModuleDetailViewModel(
    LearningPathModuleDetailDto Detail,
    string? ErrorMessage = null);

public sealed record LearningPathUpgradeViewModel(
    LearningPathSummaryDto Summary,
    int Balance,
    int UpgradeCost = 5,
    string? ErrorMessage = null)
{
    public bool CanUpgrade => Balance >= UpgradeCost;
    public int ShortBy => Math.Max(0, UpgradeCost - Balance);
    public bool IsAlreadyPremium => Summary.IsPremium;
}
