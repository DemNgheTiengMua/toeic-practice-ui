namespace Contracts;

public sealed record CefrBandInfoDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    bool IsReached,
    bool IsCurrent,
    bool IsTarget = false);
