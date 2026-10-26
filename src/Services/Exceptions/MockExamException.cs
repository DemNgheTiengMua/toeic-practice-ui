using System;

namespace Services;

public class MockExamException(string code, string message) : Exception(message)
{
    public const string InsufficientCredits = "insufficient_credits";
    public const string AttemptExpired = "attempt_expired";
    public const string ExamNotFound = "exam_not_found";
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string InvalidOption = "invalid_option";
    public const string QuestionNotFound = "question_not_found";
    public const string AttemptNotFinished = "attempt_not_finished";
    public const string PreflightRequired = "preflight_required";

    public string Code { get; } = code;
}
