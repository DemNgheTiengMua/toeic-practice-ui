using System;

namespace Services;

public class PlacementException(string code, string message) : Exception(message)
{
    public const string ExamNotFound = "exam_not_found";
    public const string PlacementNotFound = "placement_not_found";
    public const string PlacementNotActive = "placement_not_active";
    public const string PlacementExpired = "placement_expired";
    public const string PlacementNotFinished = "placement_not_finished";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string TargetBandInvalid = "target_band_invalid";

    /// <summary>#217 / PLC-35: nothing on this placement failed to grade, so there is nothing to retry.</summary>
    public const string RetryNotAllowed = "retry_not_allowed";

    /// <summary>
    /// #217 / PLC-19: the sitting measured nothing, so it was recorded as abandoned and has no result.
    /// Distinct from <see cref="PlacementNotFinished"/>, which means "still grading" - a learner told
    /// "not finished" would wait for a figure that is never coming.
    /// </summary>
    public const string PlacementAbandoned = "placement_abandoned";

    /// <summary>#213 / PLC-07: a retake inside the 24-hour cooldown. Never a lifetime cap.</summary>
    public const string PlacementCooldown = "placement_cooldown";

    /// <summary>
    /// #215 / D7, D10: a routed band cannot supply the five items its floor requires, so the sitting is
    /// refused before any attempt row exists. Never a shortened paper — a band measured on four items
    /// makes one lucky guess worth 25 points.
    /// </summary>
    public const string PlacementStockShort = "placement_stock_short";

    /// <summary>#153: the declared pace is above the 25 h/week ceiling the spec allows any plan.</summary>
    public const string WeeklyHoursTooHigh = "weekly_hours_too_high";

    /// <summary>#153: the exam date has already passed, so the goal has no runway to compute.</summary>
    public const string TargetDateInvalid = "target_date_invalid";
    public const string InsufficientCredits = "insufficient_credits";

    public string Code { get; } = code;
}
