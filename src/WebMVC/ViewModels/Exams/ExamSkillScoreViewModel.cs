using Contracts;

namespace WebMVC.Models;

public sealed record ExamSkillScoreViewModel(AttemptSkillScoreDto Score, int AccuracyPercent);
