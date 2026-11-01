using System.Globalization;
using Contracts;

namespace WebMVC.Models;
public sealed record SpeakingReviewViewModel(
    SpeakingReviewDto Review,
    string? ErrorMessage = null)
{
    /// <summary>
    /// #152: the score as the learner reads it. The stored integer is in the certificate's own units
    /// (tenths on IELTS, points on TOEIC), and this is the only place it becomes text — the page used
    /// to print the stored value, so a 7.5 arrived as "75". With no placement the page falls back to
    /// the rubric total, which is already in rubric points.
    /// </summary>
    public string ScoreDisplay => Review.ScaledScore.HasValue
        ? Contracts.ScoreDisplay.Format(Review.ScaledScore.Value, Review.ScoreDisplayFactor)
        : Review.TotalScore.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The top of the same axis, or empty when the certificate declared none.</summary>
    public string ScoreMaxDisplay => Review.ScaledScoreMax.HasValue
        ? Contracts.ScoreDisplay.Format(Review.ScaledScoreMax.Value, Review.ScoreDisplayFactor)
        : string.Empty;

    public string ScoreScaleLabel => Review.ScaledScoreMax.HasValue
        ? $"Speaking score · out of {ScoreMaxDisplay} · {Review.CertCode} scale"
        : $"Speaking score · {Review.CertCode} scale";
}
