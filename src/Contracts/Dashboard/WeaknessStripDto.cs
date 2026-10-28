
namespace Contracts;

public sealed record WeaknessStripDto(
    string Scope,
    int Id,
    string Code,
    string Name,
    string SectionName,
    int Answered,
    int Correct,
    int AccuracyPercent,
    string Tier,
    string ActionLabel,
    string ActionUrl);
