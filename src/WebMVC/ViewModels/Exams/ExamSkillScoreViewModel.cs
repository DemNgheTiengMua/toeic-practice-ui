using Contracts;

namespace WebMVC.Models;

/// <summary>
/// One skill's score on a result page. #152: the stored scaled score is in the certificate's own
/// units (tenths on IELTS), so <see cref="ScaledScoreDisplay"/> is what the page prints — never
/// <see cref="AttemptSkillScoreDto.ScaledScore"/>, which would show 65 for a 6.5.
/// </summary>
public sealed record ExamSkillScoreViewModel(AttemptSkillScoreDto Score, int AccuracyPercent, int ScoreDisplayFactor = 1)
{
    public string ScaledScoreDisplay => ScoreDisplay.Format(Score.ScaledScore, ScoreDisplayFactor);
}
