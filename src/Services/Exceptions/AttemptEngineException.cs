using System;

namespace Services;

public class AttemptEngineException(string code, string message) : Exception(message)
{
    public const string AttemptNotFound = "attempt_not_found";
    public const string AttemptNotActive = "attempt_not_active";
    public const string AttemptExpired = "attempt_expired";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string AttemptNotFinished = "attempt_not_finished";
    public const string WrongPhase = "wrong_phase";
    public const string PreflightRequired = "preflight_required";
    public const string ExamNotFound = "exam_not_found";
    /// <summary>
    /// #186: the paper's binding points at a position that holds no question. The attempt is refused
    /// before anything is written rather than created with a hole in it.
    /// </summary>
    public const string PaperNotAssembled = "paper_not_assembled";

    public string Code { get; } = code;
}
