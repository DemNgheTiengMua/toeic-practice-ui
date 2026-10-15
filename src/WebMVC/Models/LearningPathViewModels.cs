using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathIndexViewModel(
    LearningPathSummaryDto Summary,
    string? ErrorMessage = null);

public sealed record LearningPathModuleDetailViewModel(
    LearningPathModuleDetailDto Detail,
    string? ErrorMessage = null);
