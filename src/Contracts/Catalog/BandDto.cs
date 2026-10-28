
namespace Contracts;

public sealed record BandDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
