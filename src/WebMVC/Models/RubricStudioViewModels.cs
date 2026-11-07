using System.Collections.Generic;
using System.Linq;
using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #208: one editable grid row - a criterion, and the seven bands across it. <see cref="Cells"/> is
/// always seven entries in band order, whether or not the rubric has been written yet: the grid's shape
/// is the rubric's shape, so an empty cell is a cell waiting for a descriptor and not a missing column.
/// </summary>
public sealed record RubricGridRow(
    int CriterionId,
    string Code,
    string Name,
    IReadOnlyList<RubricGridCell> Cells,
    IReadOnlyList<RubricAnchorSlot> Anchors)
{
    /// <summary>How many of the seven bands carry both a descriptor and a gate. The page prints it.</summary>
    public int FilledCells => Cells.Count(cell =>
        !string.IsNullOrWhiteSpace(cell.Descriptor) && !string.IsNullOrWhiteSpace(cell.BinaryGates));
}

/// <summary>
/// One cell. <see cref="Band"/> is the rubric's CEFR band (0-6), and it is the coordinate the page's
/// <c>data-criterion</c> / <c>data-band</c> pair spells: a test that counted inputs would pass a grid of
/// seven criteria by four bands, which is why the coordinate is data and not a column position.
/// </summary>
public sealed record RubricGridCell(byte Band, string BandLabel, string Descriptor, string BinaryGates);

/// <summary>One anchor slot at band 2, 3 or 4, pre-created so the three are always visible together.</summary>
public sealed record RubricAnchorSlot(byte Band, string BandLabel, string ResponseText);

/// <summary>
/// #208: the page's ladder panel. Built from the API's verdict, so the sentence an author reads on the
/// screen is the sentence the lock trigger stores - a page that recomposed its own wording would let the
/// two disagree, and the one the author acts on would be the one with no rule behind it.
/// </summary>
public sealed record RubricLadderPanel(
    string Outcome,
    bool Accepted,
    string? Reason,
    IReadOnlyList<string> ResponsibleCriteria,
    IReadOnlyList<RubricLadderPointDto> Points,
    /// <summary>True once the ladder has run at all; false means "not plotted", which the page says.</summary>
    bool HasRun)
{
    /// <summary>Every point's composite as a percentage of the 0-6 axis, for the plot's bar heights.</summary>
    public IReadOnlyList<RubricLadderBar> Bars =>
        Points.Select(point => new RubricLadderBar(
            point.Band,
            RubricBands.Label(point.Band),
            point.Composite,
            point.IsVerboseTrap,
            // Composite is already on the 0-6 axis; a bar is a share of that axis, clamped so a
            // rounding surplus cannot overflow the track.
            (int)System.Math.Clamp(System.Math.Round(point.Composite / 6m * 100m), 0m, 100m),
            // D17 gate 4: adjacent benchmarks are at least 0.65 apart. The seam of every pair is what
            // the plot marks, so a flatline is visible as a missing or short gap rather than inferred.
            point.IsVerboseTrap ? "trap" : null)).ToList();
}

/// <summary>One bar of the ladder plot.</summary>
public sealed record RubricLadderBar(
    byte Band, string BandLabel, decimal Composite, bool IsVerboseTrap, int Percent, string? Marker);

/// <summary>
/// #208 / S-08: the rubric and grader calibration studio.
/// <para>
/// <see cref="Rows"/> is the whole ticket's first criterion in one object: four criteria by seven bands,
/// each band a cell with its own coordinate. The page renders 28 editable cells from it, and the test
/// asserts the coordinate rather than the count.
/// </para>
/// <para>
/// <see cref="CanLock"/> is false while any cell is empty. It is an affordance, not the rule: the lock
/// button's real guard is the service and the schema trigger, and a disabled button is a courtesy that
/// would be a lie if it were the only check.
/// </para>
/// </summary>
public sealed record RubricStudioViewModel(
    int CertId,
    string CertCode,
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    IReadOnlyList<RubricGridRow> Rows,
    RubricLadderPanel Ladder,
    bool IsLocked,
    string? LockedAt,
    string? ErrorMessage)
{
    public int CellCount => Rows.Sum(row => row.Cells.Count);

    public int FilledCellCount => Rows.Sum(row => row.FilledCells);

    public bool AllCellsFilled => Rows.Count > 0 && Rows.All(row => row.FilledCells == row.Cells.Count);

    /// <summary>Every criterion's three anchor bands are written. A locked rubric is graded against them.</summary>
    public bool AllAnchorsWritten => Rows.Count > 0 && Rows.All(row =>
        row.Anchors.Count == RubricBands.AnchorBands.Count && row.Anchors.All(a => !string.IsNullOrWhiteSpace(a.ResponseText)));

    public string ModalityLabel => Modality == "speaking" ? "Speaking" : "Writing";

    /// <summary>Bands 0-6, in order, for the grid's header row.</summary>
    public IReadOnlyList<string> BandLabels => RubricBands.All.Select(RubricBands.Label).ToList();
}

/// <summary>
/// The one translation step: the API's rows into a fixed 4x7 grid with the empty cells pre-created.
/// Nothing is computed here that the service already decided - the verdict, the lock and the reason all
/// arrive from the API.
/// </summary>
public static class RubricStudioPresentation
{
    public static RubricStudioViewModel For(RubricStudioDto studio, string? errorMessage = null) =>
        new(
            studio.CertId,
            studio.CertCode,
            studio.SkillId,
            studio.SkillCode,
            studio.SkillName,
            studio.Modality,
            studio.Criteria.Select(Row).ToList(),
            Panel(studio.Ladder),
            studio.IsLocked,
            studio.LockedAt?.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            errorMessage);

    private static RubricGridRow Row(RubricCriterionDto criterion) =>
        new(
            criterion.CriterionId,
            criterion.Code,
            criterion.Name,
            // Seven entries always, in band order: a rubric missing band 4 renders a band-4 cell with
            // nothing in it, which is the hole the author has to fill - not a column that vanishes.
            RubricBands.All.Select(band => new RubricGridCell(
                band,
                RubricBands.Label(band),
                criterion.Cells.FirstOrDefault(cell => cell.Band == band)?.Descriptor ?? string.Empty,
                criterion.Cells.FirstOrDefault(cell => cell.Band == band)?.BinaryGates ?? string.Empty)).ToList(),
            RubricBands.AnchorBands.Select(band => new RubricAnchorSlot(
                band,
                RubricBands.Label(band),
                criterion.Anchors.FirstOrDefault(anchor => anchor.Band == band)?.ResponseText ?? string.Empty)).ToList());

    private static RubricLadderPanel Panel(RubricLadderDto? ladder) =>
        ladder is null
            ? new RubricLadderPanel(RubricLadderOutcomes.NotPlotted, false, null, [], [], false)
            : new RubricLadderPanel(
                ladder.Outcome,
                ladder.Accepted,
                ladder.Reason,
                ladder.ResponsibleCriteria,
                ladder.Points,
                true);
}
