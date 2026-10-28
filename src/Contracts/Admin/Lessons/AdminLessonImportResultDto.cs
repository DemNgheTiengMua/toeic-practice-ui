
namespace Contracts;

public sealed record AdminLessonImportResultDto(
    int CreatedCount,
    int UpdatedCount,
    int TotalCardsImported,
    IReadOnlyList<string> Messages);
