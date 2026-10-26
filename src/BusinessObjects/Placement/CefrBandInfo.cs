namespace BusinessObjects;

public sealed record CefrBandInfo(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    bool IsReached,
    bool IsCurrent,
    bool IsTarget = false);
