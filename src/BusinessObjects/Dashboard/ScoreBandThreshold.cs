
namespace BusinessObjects;

public sealed record ScoreBandThreshold(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
