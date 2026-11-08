using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #213 / SCR-PL-01: the gate before the sitting. It answers three questions and nothing else — what
/// will be measured, what may be carried over, and whether a retake is open yet.
/// <para>
/// <b>No price is computed here.</b> Placement is free; a balance on this page would be the way a price
/// reaches a free flow, so the view model has no wallet dependency at all.
/// </para>
/// <para>
/// #221 / D18: the equipment panel is scoped to the skills this paper will actually measure. A
/// reading-only certificate gets <c>null</c>, so the page renders no microphone control at all -
/// requesting a permission the sitting will never use devalues the next genuine prompt.
/// </para>
/// </summary>
public sealed record PlacementPreflightPageViewModel(
    PlacementPreflightViewDto Gate,
    string? ErrorMessage = null)
{
    /// <summary>The one primary action: start. Everything else on the page is information.</summary>
    public bool CanStart => Gate.IsStartable;

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
}
