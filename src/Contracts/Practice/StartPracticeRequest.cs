namespace Contracts;

public sealed record StartPracticeRequest(
    int SectionId,
    int? QuestionCount = 20,
    string? QuestionSource = "all",
    string? FeedbackMode = "immediate");
