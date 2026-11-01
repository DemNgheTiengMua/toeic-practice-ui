using Contracts;

namespace WebMVC.Models;

public sealed record PracticeCatalogViewModel(PracticeCatalogDto Catalog, string? ErrorMessage = null)
{
    public AudioEquipmentCheckModel AudioCheck { get; } = new(ShowInput: false, ShowOutput: true, Title: "Listening practice audio check");
    public string UnfinishedStartedAtDisplay { get; init; } = "";
    // #149: the empty state asks whether the certificate has any sections at all, not whether it has
    // Listening or Reading ones - a certificate with a single Writing skill used to read "No questions".
    public bool HasAnySections => Catalog.ListeningSections.Count + Catalog.ReadingSections.Count + Catalog.OtherSections.Count + SpeakingSections.Count > 0;
    public IReadOnlyList<PracticeSectionGroupViewModel> OtherSectionGroups { get; } = Catalog.OtherSections
        .GroupBy(s => s.SkillName).Select(g => new PracticeSectionGroupViewModel(g.Key, g.ToList())).ToList();
    // #150: a spoken answer cannot be typed, so speaking sections are listed separately and linked to
    // the recorded, AI-graded speaking practice instead of being offered a typed drill.
    public IReadOnlyList<PracticeSectionDto> SpeakingSections => Catalog.SpeakingSections ?? [];
    public int? SpeakingPracticeExamId { get; } = Catalog.SpeakingPracticeExamId;
    // The start form is meaningless without a section button in it: a Speaking-only certificate would
    // otherwise meet three dropdowns it can never submit.
    public bool HasDrillableSections => Catalog.ListeningSections.Count + Catalog.ReadingSections.Count + Catalog.OtherSections.Count > 0;
}
