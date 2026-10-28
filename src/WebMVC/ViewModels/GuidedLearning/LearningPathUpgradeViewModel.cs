using Contracts;

namespace WebMVC.Models;

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
