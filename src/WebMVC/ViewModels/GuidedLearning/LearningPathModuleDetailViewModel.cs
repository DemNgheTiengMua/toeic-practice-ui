using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathModuleDetailViewModel(
    LearningPathModuleDetailDto Detail,
    string? ErrorMessage = null)
{
    public IReadOnlyList<LearningPathStepViewModel> AdditionalSteps { get; } = Detail.Steps
        .Where(step => step.ItemType != PathModuleItemTypeDto.PracticeSection).Select(step => new LearningPathStepViewModel(step)).ToArray();
    public int RemainingQuestions => Math.Max(0, Detail.TotalQuestions - Detail.AnsweredQuestions);
    public int ProgressPercent => Detail.TotalQuestions > 0 ? (int)Math.Round((double)Detail.AnsweredQuestions / Detail.TotalQuestions * 100) : 0;
    public string FormattedHeading => NormalizeHeading(Detail.ModuleIndex, Detail.Title);

    public static string NormalizeTitle(string? title)
    {
        var rawTitle = title ?? "";
        return System.Text.RegularExpressions.Regex.Replace(rawTitle, @"^Module\s+\d+\s*:\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    public static string NormalizeHeading(int moduleIndex, string? title)
    {
        var cleanTitle = NormalizeTitle(title);
        return $"Module {moduleIndex}: {cleanTitle}";
    }
}
