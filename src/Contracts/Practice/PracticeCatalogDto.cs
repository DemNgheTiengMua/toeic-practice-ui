namespace Contracts;

public sealed record PracticeCatalogDto(
    CertificateOptionDto Certificate,
    IReadOnlyList<PracticeSectionDto> ListeningSections,
    IReadOnlyList<PracticeSectionDto> ReadingSections,
    IReadOnlyList<PracticeSectionDto> OtherSections,
    PracticeSectionDto? WeakestSection,
    PracticeSessionDto? UnfinishedSession = null);
