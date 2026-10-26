using Contracts;

namespace WebMVC.Models;

public sealed record ExamReviewViewModel(AttemptReviewDto Review, string? ErrorMessage = null)
{
    public string SubmittedAtDisplay { get; init; } = string.Empty;
    public string SkillScoresDisplay { get; init; } = string.Empty;
    public IReadOnlyList<ExamReviewSectionViewModel> Sections { get; init; } = [];
    public IReadOnlyList<ExamReviewQuestionViewModel> Questions { get; init; } = [];
    public IReadOnlyList<ExamReviewNavigatorViewModel> Navigator { get; init; } = [];
}
