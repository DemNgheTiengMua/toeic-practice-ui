using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>Sections grouped by modality for the practice selection screen.</summary>
public sealed record PracticeSectionsView(
    CertificateOption Certificate,
    IReadOnlyList<PracticeSectionItem> ListeningSections,
    IReadOnlyList<PracticeSectionItem> ReadingSections,
    IReadOnlyList<PracticeSectionItem> OtherSections,
    PracticeSectionItem? WeakestSection,
    PracticeSessionInfo? UnfinishedSession = null);
