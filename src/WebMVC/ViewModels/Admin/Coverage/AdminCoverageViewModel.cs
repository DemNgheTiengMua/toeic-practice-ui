using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #202 / S-01: the warehouse coverage matrix, grouped by skill for reading and nothing else.
/// <para>
/// The grouping is a layout choice — one table per skill — not an arithmetic one. Every figure this
/// type exposes is a column of <see cref="AdminCoverageCellDto"/> as the database view produced it:
/// there is no total, no percentage and no "remaining" anywhere, because a second arithmetic path over
/// the same stock is how a coverage screen starts disagreeing with the pool it reports.
/// </para>
/// <para>
/// A cell with zero stock arrives as a cell with <c>Deficit == TargetItemCount</c>; it is never a
/// missing element. That is the view's guarantee, and <see cref="AdminCoverageCellViewModel.IsEmpty"/>
/// exists so the markup can paint it differently without deciding anything itself.
/// </para>
/// </summary>
public sealed record AdminCoverageViewModel(
    IReadOnlyList<AdminCoverageSkillViewModel> Skills,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Set when the payload carried no skill at all. The view tests this rather than counting the
    /// list, because a count in the markup is a number the view layer computed (criterion 3).
    /// </summary>
    public bool HasNoCells => Skills.Count == 0;
}

/// <summary>One skill's table: its topics down the side, the six levels across the top.</summary>
public sealed record AdminCoverageSkillViewModel(
    string Modality,
    string ModalityLabel,
    IReadOnlyList<string> LevelLabels,
    IReadOnlyList<AdminCoverageRowViewModel> Rows);

/// <summary>One topic's line: the topic, then one plaque per level.</summary>
public sealed record AdminCoverageRowViewModel(
    int TopicId,
    string TopicCode,
    string TopicName,
    IReadOnlyList<AdminCoverageCellViewModel> Cells);

/// <summary>
/// One plaque on the grid. <see cref="DeficitText"/> is the reason this type exists: the cell says what
/// it is short of in words as well as in a figure, because colour is always the second layer of
/// information (D19a) and a lone "-4" names no noun.
/// </summary>
public sealed record AdminCoverageCellViewModel(AdminCoverageCellDto Cell, string CefrLevelLabel)
{
    public bool IsEmpty => Cell.ActiveItemCount == 0;

    public bool IsSatisfied => Cell.Deficit == 0;

    public string DeficitText => Cell.Deficit == 0 ? "0 short" : $"{Cell.Deficit} short";

    /// <summary>
    /// The cell's paint. A state rather than an expression in the markup: the empty cell is the one
    /// reading that must never be ambiguous, so the three cases are named here and the view only
    /// prints the name.
    /// </summary>
    public string StateClass => IsEmpty ? "coverage-cell--empty"
        : IsSatisfied ? "coverage-cell--met"
        : "coverage-cell--short";
}

/// <summary>
/// Builds the screen's shape from the matrix. It groups rows and labels codes; it computes no figure,
/// which is why it can live here rather than in the service.
/// </summary>
public static class AdminCoveragePresentation
{
    public static AdminCoverageViewModel From(
        AdminCoverageMatrixDto matrix,
        Func<string?, string> levelLabel,
        Func<string?, string> modalityLabel) =>
        new(matrix.Cells
            .GroupBy(cell => cell.Modality, StringComparer.Ordinal)
            .Select(skill => new AdminCoverageSkillViewModel(
                skill.Key,
                modalityLabel(skill.Key),
                // Level codes sort as A1, A2, B1, B2, C1, C2, which is the ladder order the header
                // wants; the axis comes from the rows themselves so a level the view dropped is a
                // missing column rather than an invented one.
                skill.Select(cell => cell.CefrLevel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                skill
                    .GroupBy(cell => cell.TopicId)
                    .Select(topic => new AdminCoverageRowViewModel(
                        topic.Key,
                        topic.First().TopicCode,
                        topic.First().TopicName,
                        topic
                            .OrderBy(cell => cell.CefrLevel, StringComparer.Ordinal)
                            .Select(cell => new AdminCoverageCellViewModel(cell, levelLabel(cell.CefrLevel)))
                            .ToList()))
                    .OrderBy(row => row.TopicCode, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(skill => skill.Modality, StringComparer.Ordinal)
            .ToList());
}
