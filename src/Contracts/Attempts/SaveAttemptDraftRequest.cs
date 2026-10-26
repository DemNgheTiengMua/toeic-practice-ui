namespace Contracts;

public sealed record SaveAttemptDraftRequest(
    int QuestionId,
    string? ResponseText);
