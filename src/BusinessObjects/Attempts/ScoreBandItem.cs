namespace BusinessObjects;

public sealed record ScoreBandItem(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);
