using Contracts;

namespace WebMVC.Models;

public sealed record PracticeCatalogViewModel(PracticeCatalogDto Catalog, string? ErrorMessage = null)
{
    public AudioEquipmentCheckModel AudioCheck { get; } = new(ShowInput: false, ShowOutput: true, Title: "Listening practice audio check");
    public string UnfinishedStartedAtDisplay { get; init; } = "";
    // #149: the empty state asks whether the certificate has any sections at all, not whether it has
    // Listening or Reading ones - a certificate with a single Writing skill used to read "No questions".
    public bool HasAnySections { get; } = Catalog.ListeningSections.Count + Catalog.ReadingSections.Count + Catalog.OtherSections.Count > 0;
    // Only listening and reading sections can be drilled; practice refuses the other modalities.
    public bool HasDrillableSections { get; } = Catalog.ListeningSections.Count + Catalog.ReadingSections.Count > 0;
    public IReadOnlyList<PracticeSectionGroupViewModel> OtherSectionGroups { get; } = Catalog.OtherSections
        .GroupBy(s => s.SkillName).Select(g => new PracticeSectionGroupViewModel(g.Key, g.ToList())).ToList();
}
