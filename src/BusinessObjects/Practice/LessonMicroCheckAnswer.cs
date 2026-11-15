namespace BusinessObjects;

/// <summary>#155: the result of grading a lesson's micro-check on the server. The correct option is only
/// revealed in this reply - the page no longer carries it as a data attribute for the browser to grade
/// against.
/// <para>
/// #373: <see cref="IsCorrect"/> is the verdict of the attempt just submitted, not the stored one. The
/// first answer is the only one that moves mastery, and <see cref="AlreadyGraded"/> says so.
/// </para></summary>
public sealed record LessonMicroCheckAnswer(
    bool IsCorrect,
    string CorrectOption,
    string? Explanation,
    int? CategoryId,
    bool AlreadyGraded = false);
