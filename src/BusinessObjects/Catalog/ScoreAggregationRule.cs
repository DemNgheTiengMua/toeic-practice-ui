
namespace BusinessObjects;

/// <summary>
/// CK_Certificates_ScoreAggregationRule: sum, avg_half_step, avg_whole.
/// <para>
/// #171 removed <c>independent</c>. It returned a total of 0 for every attempt and the trigger that
/// checks a total against its skills exempted it, so an administrator could author a certificate
/// whose learners could never be scored and nothing anywhere said so. Every page that reads a score
/// (the band table, the result page, the path planner) assumes one total per attempt, so the honest
/// fix was to drop the rule rather than invent a second scoring model for it.
/// </para>
/// </summary>
public enum ScoreAggregationRule
{
    Sum,
    AvgHalfStep,
    AvgWhole,
}
