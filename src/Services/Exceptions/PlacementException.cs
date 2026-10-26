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

    public string Code { get; } = code;
}
