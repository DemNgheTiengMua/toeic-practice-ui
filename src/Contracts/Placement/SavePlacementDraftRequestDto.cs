namespace Contracts;

public sealed record SavePlacementDraftRequestDto(
    int QuestionId,
    string? ResponseText);
