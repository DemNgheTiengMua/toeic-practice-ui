using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #213 / SCR-PL-01, #290: the confirmation gate before starting a placement sitting.
/// <para>
/// Presents the two scopes (<see cref="PlacementScopeDto.AllSkills"/> and
/// <see cref="PlacementScopeDto.ListeningAndReading"/>) as an explicit choice with configured prices
/// from the API, names the skills each scope leaves out, and enforces short-balance refusal before
/// anything starts.
/// </para>
/// <para>
/// #221 / D18: the equipment panel is scoped to the skills this paper will actually measure. A
/// reading-only certificate gets <c>null</c>, and choosing <see cref="PlacementScopeDto.ListeningAndReading"/>
/// hides microphone checks on the page when only objective skills are in scope.
/// </para>
/// </summary>
public sealed record PlacementPreflightPageViewModel(
    PlacementPreflightViewDto Gate,
    string? ErrorMessage = null,
    int? ExamIdOverride = null)
{
    public int? EffectiveExamId => ExamIdOverride ?? Gate.ExamId;
    public bool IsRetake => Gate.IsRetake;
    public int CreditBalance => Gate.CreditBalance ?? 0;
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString("N");
    public int ReusableOfferCount => Gate.Offers.Count(o => o.CanReuse);

    /// <summary>The primary action is only available when the cooldown has elapsed and at least one scope is affordable.</summary>
    public bool CanStart => Gate.IsStartable;

    public bool CanConfirmAnyScope => Gate.IsStartable && ScopeChoices.Any(c => c.CanAfford);
    public bool IsShortForAllScopes => Gate.IsRetake && !ScopeChoices.Any(c => c.CanAfford);
    public bool IsShortForSomeScope => Gate.IsRetake && ScopeChoices.Any(c => !c.CanAfford);
    public int MinimumRequiredCredits => Gate.IsRetake ? ScopeChoices.Min(c => c.PriceCredits) : 0;
    public int ShortByMinimum => Gate.IsRetake ? Math.Max(0, MinimumRequiredCredits - CreditBalance) : 0;

    public IReadOnlyList<PlacementScopeChoiceViewModel> ScopeChoices { get; } = BuildScopeChoices(Gate);

    public bool CanConfirmScope(PlacementScopeDto scope) =>
        Gate.IsStartable && (!Gate.IsRetake || CreditBalance >= PriceFor(scope));

    public int PriceFor(PlacementScopeDto scope) => !Gate.IsRetake
        ? 0
        : scope == PlacementScopeDto.ListeningAndReading
            ? Gate.ListeningAndReadingRetakeCredits
            : Gate.AllSkillsRetakeCredits;

    public bool AllSkillsRequiresAudio => Gate.RequiresAudio || Gate.Offers.Any(o => o.RequiresAudio);
    public bool AllSkillsRequiresMicrophone => Gate.RequiresMicrophone || Gate.Offers.Any(o => o.RequiresMicrophone);

    public bool ListeningAndReadingRequiresAudio =>
        Gate.Offers.Any(o => IsObjectiveModality(o.Modality) && o.RequiresAudio);

    public bool ListeningAndReadingRequiresMicrophone =>
        Gate.Offers.Any(o => IsObjectiveModality(o.Modality) && o.RequiresMicrophone);

    /// <summary>
    /// The pre-sitting equipment panel: microphone for a skill that has to be measured, output for a
    /// skill that plays audio. Null when this sitting needs neither, which is what suppresses the
    /// whole panel on a reading-only paper rather than leaving an empty one.
    /// </summary>
    public AudioEquipmentCheckModel? AudioEquipment => Gate.RequiresMicrophone || Gate.RequiresAudio
        ? new AudioEquipmentCheckModel(
            ShowInput: Gate.RequiresMicrophone,
            ShowOutput: Gate.RequiresAudio,
            Title: "Before you start: audio equipment")
        : null;

    /// <summary>A carried result from day 60 is aged and says so; the daily date is the screen's clock.</summary>
    public string MeasuredOn(PlacementSkillOfferDto offer) =>
        offer.MeasuredAt is { } at ? at.ToLocalTime().ToString("d MMM yyyy") : "—";

    /// <summary>`B2 (78/100)` — a level and a position, never a band and never a bare percentage.</summary>
    public string LevelAndPosition(PlacementSkillOfferDto offer) =>
        offer is { LevelCode: { } level, PositionInBand: { } position }
            ? $"{level} ({position}/100)"
            : "Not measured";

    public string CooldownMessage => Gate.CooldownUntil is { } until
        ? $"A retake opens at {until.ToLocalTime():d MMM yyyy HH:mm}. There is no limit on how many placements you take."
        : "You can start now.";

    public static string FormatCredits(int credits) =>
        credits == 0 ? "Free" : credits == 1 ? "1 credit" : $"{credits} credits";

    private static bool IsObjectiveModality(string? modality) =>
        string.Equals(modality, "listening", StringComparison.OrdinalIgnoreCase)
        || string.Equals(modality, "reading", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<PlacementScopeChoiceViewModel> BuildScopeChoices(PlacementPreflightViewDto gate)
    {
        var allNames = gate.Offers.Select(o => o.SkillName).ToList();
        var lrNames = gate.Offers.Where(o => IsObjectiveModality(o.Modality)).Select(o => o.SkillName).ToList();
        var excludedForLr = gate.Offers.Where(o => !IsObjectiveModality(o.Modality)).Select(o => o.SkillName).ToList();

        if (allNames.Count == 0)
        {
            allNames = ["Listening", "Reading", "Speaking", "Writing"];
        }

        if (lrNames.Count == 0)
        {
            lrNames = ["Listening", "Reading"];
        }

        if (excludedForLr.Count == 0 && !gate.Offers.Any())
        {
            excludedForLr = ["Speaking", "Writing"];
        }

        var balance = gate.CreditBalance ?? 0;
        var allPrice = gate.IsRetake ? gate.AllSkillsRetakeCredits : 0;
        var lrPrice = gate.IsRetake ? gate.ListeningAndReadingRetakeCredits : 0;

        return
        [
            new PlacementScopeChoiceViewModel(
                Scope: PlacementScopeDto.AllSkills,
                Value: nameof(PlacementScopeDto.AllSkills),
                RadioId: "scope-all-skills",
                Title: "All four skills",
                PriceCredits: allPrice,
                PriceLabel: FormatCredits(allPrice),
                IncludedSkills: allNames,
                ExcludedSkills: [],
                IncludedSummary: string.Join(", ", allNames),
                ExcludedSummary: "None — every examined skill is measured",
                CanAfford: !gate.IsRetake || balance >= allPrice,
                ShortBy: gate.IsRetake ? Math.Max(0, allPrice - balance) : 0),
            new PlacementScopeChoiceViewModel(
                Scope: PlacementScopeDto.ListeningAndReading,
                Value: nameof(PlacementScopeDto.ListeningAndReading),
                RadioId: "scope-listening-reading",
                Title: "Listening and Reading only",
                PriceCredits: lrPrice,
                PriceLabel: FormatCredits(lrPrice),
                IncludedSkills: lrNames,
                ExcludedSkills: excludedForLr,
                IncludedSummary: string.Join(", ", lrNames),
                ExcludedSummary: excludedForLr.Count > 0
                    ? string.Join(", ", excludedForLr)
                    : "None — this certificate only examines Listening and Reading",
                CanAfford: !gate.IsRetake || balance >= lrPrice,
                ShortBy: gate.IsRetake ? Math.Max(0, lrPrice - balance) : 0),
        ];
    }
}

public sealed record PlacementScopeChoiceViewModel(
    PlacementScopeDto Scope,
    string Value,
    string RadioId,
    string Title,
    int PriceCredits,
    string PriceLabel,
    IReadOnlyList<string> IncludedSkills,
    IReadOnlyList<string> ExcludedSkills,
    string IncludedSummary,
    string ExcludedSummary,
    bool CanAfford,
    int ShortBy);
