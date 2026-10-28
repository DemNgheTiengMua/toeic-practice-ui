
namespace BusinessObjects;

/// <summary>The most recent graded mock attempt's score and band snapshot.</summary>
public sealed record LatestResult(int TotalScore, string? BandCode, string? BandName, DateTimeOffset TakenAt);
