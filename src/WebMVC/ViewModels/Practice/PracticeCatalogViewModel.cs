using Contracts;

namespace WebMVC.Models;

public sealed record PracticeCatalogViewModel(PracticeCatalogDto Catalog, string? ErrorMessage = null)
{
    public AudioEquipmentCheckModel AudioCheck { get; } = new(ShowInput: false, ShowOutput: true, Title: "Listening practice audio check");
    public string UnfinishedStartedAtDisplay { get; init; } = "";
    public IReadOnlyList<PracticeSectionGroupViewModel> OtherSectionGroups { get; } = Catalog.OtherSections
        .GroupBy(s => s.SkillName).Select(g => new PracticeSectionGroupViewModel(g.Key, g.ToList())).ToList();
}
