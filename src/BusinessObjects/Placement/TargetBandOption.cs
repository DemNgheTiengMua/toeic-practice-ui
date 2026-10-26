namespace BusinessObjects;

public sealed record TargetBandOption(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    string? Description,
    bool IsCurrent,
    bool IsTarget);
