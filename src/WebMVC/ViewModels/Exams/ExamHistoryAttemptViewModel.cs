using Contracts;

namespace WebMVC.Models;

public sealed record ExamHistoryAttemptViewModel(
    ExamHistoryItemDto Attempt, string SubmittedAtDisplay, string SkillScoresDisplay);
