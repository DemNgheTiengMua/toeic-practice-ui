namespace Contracts;

public sealed record ScoreBandItemDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);
