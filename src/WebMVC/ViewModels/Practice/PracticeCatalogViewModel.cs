using Contracts;

namespace WebMVC.Models;

public sealed record PracticeCatalogViewModel(PracticeCatalogDto Catalog, string? ErrorMessage = null)
{
    /// <summary>#161: the input check names the skills the learner's own certificate drills, not
    /// "Listening practice" for a page that may hold no listening section at all.</summary>
    public AudioEquipmentCheckModel AudioCheck { get; } = new(ShowInput: false, ShowOutput: true,
        Title: "Audio check for the sections you can drill");

    public string UnfinishedStartedAtDisplay { get; init; } = "";

    /// <summary>#159 review: the cooldown card told a UTC+7 learner "next due 02:00 UTC" when their
    /// clock said 09:00. Instants are stored UTC and only <see cref="WebMVC.Formatters.DateTimeFormatter"/>
    /// converts them for display, so the controller formats them here and the view prints a string.
    /// Keyed by section id because that is what the card renders one at a time.</summary>
    public IReadOnlyDictionary<int, string> NextReviewDueDisplay { get; init; } =
        new Dictionary<int, string>();

    // #149: the empty state asks whether the certificate has any sections at all, not whether it has
    // Listening or Reading ones - a certificate with a single Writing skill used to read "No questions".
    public bool HasAnySections => Catalog.ListeningSections.Count + Catalog.ReadingSections.Count
        + Catalog.OtherSections.Count + SpeakingSections.Count > 0;

    // #150: a spoken answer cannot be typed, so speaking sections are listed separately and linked to
    // the recorded, AI-graded practice instead of being offered a typed drill.
    public IReadOnlyList<PracticeSectionDto> SpeakingSections => Catalog.SpeakingSections ?? [];

    public int? SpeakingPracticeExamId { get; } = Catalog.SpeakingPracticeExamId;

    /// <summary>#161: the heading over the speaking cards is the name of the skill that owns them, not
    /// the literal word "Speaking" - that word is the certificate's to choose.</summary>
    public string SpeakingSectionName => SpeakingSections.Count > 0 ? SpeakingSections[0].SkillName : "Speaking";

    /// <summary>#161: every section that can be drilled, grouped under the name of the skill that owns
    /// it and kept in the order the service delivered them (skill display order, then section order). The
    /// page used to print a literal "Listening" heading and a literal "Reading" one, so a certificate
    /// whose skills are called something else was described by two headings naming neither.</summary>
    public IReadOnlyList<PracticeSectionGroupViewModel> DrillableSectionGroups { get; } =
        Catalog.ListeningSections.Concat(Catalog.ReadingSections).Concat(Catalog.OtherSections)
            .GroupBy(s => s.SkillName)
            .Select(g => new PracticeSectionGroupViewModel(g.Key, g.ToList()))
            .ToList();

    // The start form is meaningless without a section button in it: a Speaking-only certificate would
    // otherwise meet three dropdowns it can never submit.
    public bool HasDrillableSections => DrillableSectionGroups.Count > 0;
}
