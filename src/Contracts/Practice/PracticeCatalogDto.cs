namespace Contracts;

/// <summary>#150: <paramref name="SpeakingSections"/> is separate because speaking has no typed
/// drill - a spoken answer cannot be typed - so the page links it to the recorded, AI-graded
/// speaking practice instead, whose exam is <paramref name="SpeakingPracticeExamId"/>.</summary>
public sealed record PracticeCatalogDto(
    CertificateOptionDto Certificate,
    IReadOnlyList<PracticeSectionDto> ListeningSections,
    IReadOnlyList<PracticeSectionDto> ReadingSections,
    IReadOnlyList<PracticeSectionDto> OtherSections,
    PracticeSectionDto? WeakestSection,
    PracticeSessionDto? UnfinishedSession = null,
    IReadOnlyList<PracticeSectionDto>? SpeakingSections = null,
    int? SpeakingPracticeExamId = null);
