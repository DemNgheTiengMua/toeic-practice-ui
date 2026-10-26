namespace Contracts;

public sealed record SubmitAttemptDraftRequest(
    int QuestionId,
    string? ResponseText);
