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

    /// <summary>#213 / PLC-07: a retake inside the 24-hour cooldown. Never a lifetime cap.</summary>
    public const string PlacementCooldown = "placement_cooldown";

    /// <summary>#153: the declared pace is above the 25 h/week ceiling the spec allows any plan.</summary>
    public const string WeeklyHoursTooHigh = "weekly_hours_too_high";

    /// <summary>#153: the exam date has already passed, so the goal has no runway to compute.</summary>
    public const string TargetDateInvalid = "target_date_invalid";

    public string Code { get; } = code;
}
