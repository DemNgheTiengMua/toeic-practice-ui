using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>Sections grouped by modality for the practice selection screen.</summary>
public sealed record PracticeSectionsView(
    CertificateOption Certificate,
    IReadOnlyList<PracticeSectionItem> ListeningSections,
    IReadOnlyList<PracticeSectionItem> ReadingSections,
    IReadOnlyList<PracticeSectionItem> OtherSections,
    PracticeSectionItem? WeakestSection,
    PracticeSessionInfo? UnfinishedSession = null,
    IReadOnlyList<PracticeSectionItem>? SpeakingSections = null,
    int? SpeakingPracticeExamId = null)
{
    /// <summary>#150: speaking has no typed drill, so the page sends it to its recorded, AI-graded
    /// practice. An empty list keeps the old construction sites source-compatible.</summary>
    public IReadOnlyList<PracticeSectionItem> Speaking => SpeakingSections ?? [];
}
