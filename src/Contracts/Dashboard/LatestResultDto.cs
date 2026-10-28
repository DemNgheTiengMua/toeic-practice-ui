
namespace Contracts;

public sealed record LatestResultDto(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);
