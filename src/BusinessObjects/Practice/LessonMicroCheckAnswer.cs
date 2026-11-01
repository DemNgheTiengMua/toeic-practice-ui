namespace BusinessObjects;

/// <summary>#155: the result of grading a lesson's micro-check on the server. The correct option is only
/// revealed in this reply - the page no longer carries it as a data attribute for the browser to grade
/// against.</summary>
public sealed record LessonMicroCheckAnswer(
    bool IsCorrect,
    string CorrectOption,
    string? Explanation,
    int? CategoryId);
