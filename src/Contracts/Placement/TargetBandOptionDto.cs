namespace Contracts;

public sealed record TargetBandOptionDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    string? Description,
    bool IsCurrent,
    bool IsTarget);
