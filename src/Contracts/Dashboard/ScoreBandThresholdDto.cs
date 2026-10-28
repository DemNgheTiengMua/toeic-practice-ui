
namespace Contracts;

public sealed record ScoreBandThresholdDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
