using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #238 / SCR-PRAC-01: the objective practice hub's matrix. Three guards, each with the wrong input
/// that would pass a weaker assertion:
/// <list type="bullet">
/// <item>a bool mastery — four states, so a state rendered from "is it mastered" collapses three of them;</item>
/// <item>top-three by row order — the plan's priority is a set, not the first three rows a query returned;</item>
/// <item>a dimmed cell with no readable reopen time — dimming must not swallow the only fact that stops
/// the cell reading as deleted.</item>
/// </list>
/// </summary>
public sealed class PracticeObjectiveHubTests
{
    private static PracticeCatalogDto EmptyCatalog() =>
        new(new CertificateOptionDto("DEMO", "DEMO"), [], [], [], null);

    private static PracticeCellDto Cell(int topicId, string code, string level,
        PracticeCellMasteryDto mastery, bool prioritised = false, DateTimeOffset? reopens = null) =>
        new(topicId, code, code, ModalityDto.Reading, level, mastery, prioritised, reopens,
            IsResting: reopens is not null);

    private static PracticeCatalogViewModel Model(params PracticeCellDto[] cells) =>
        new(EmptyCatalog())
        {
            Matrix = cells.Select(c => new PracticeMatrixRowViewModel(
                c.TopicCode, c.TopicName, c.LevelCode, c.Mastery.ToString(),
                PracticeMatrixRowViewModel.LabelFor(c.Mastery.ToString()), c.IsPrioritised, c.IsRestingNow,
                c.ReopensAt is { } r ? "09:00 05 Nov" : null)).ToList(),
        };

    /// <summary>
    /// The guard against "mastery rendered from a bool". Four cells, four distinct states; a model that
    /// derived its label from IsMastered would print two of these identically. The input that would pass
    /// the weaker test is four cells that happen to differ only in colour.
    /// </summary>
    [Fact]
    public void Every_one_of_the_four_mastery_states_renders_its_own_label()
    {
        var model = Model(
            Cell(1, "R-DET", "A2", PracticeCellMasteryDto.Untested),
            Cell(2, "R-INF", "B1", PracticeCellMasteryDto.Learning),
            Cell(3, "R-GAT", "B2", PracticeCellMasteryDto.Reviewing),
            Cell(4, "R-GRAM", "C1", PracticeCellMasteryDto.Mastered));

        Assert.Equal(
            ["Untested", "Learning", "Reviewing", "Mastered"],
            model.Matrix.Select(r => r.MasteryLabel));
        Assert.Equal(4, model.Matrix.Select(r => r.MasteryState).Distinct().Count());
    }

    /// <summary>
    /// The guard against "top-three by whatever the query returned first". Three cells are prioritised
    /// but sit at positions 2, 4 and 5; a model that took the first three would highlight 1, 2, 3.
    /// The wrong input is a fixture whose priority and row order agree.
    /// </summary>
    [Fact]
    public void The_highlighted_cells_are_the_plans_three_not_the_first_three_rows()
    {
        var model = Model(
            Cell(1, "R-GIST", "A2", PracticeCellMasteryDto.Learning),
            Cell(2, "R-DET", "B1", PracticeCellMasteryDto.Learning, prioritised: true),
            Cell(3, "R-INF", "B1", PracticeCellMasteryDto.Reviewing),
            Cell(4, "R-ATT", "B2", PracticeCellMasteryDto.Reviewing, prioritised: true),
            Cell(5, "R-COH", "B2", PracticeCellMasteryDto.Untested, prioritised: true));

        Assert.Equal(3, model.PrioritisedCellCount);
        Assert.Equal(
            ["R-DET", "R-ATT", "R-COH"],
            model.Matrix.Where(r => r.IsPrioritised).Select(r => r.TopicCode));
        Assert.False(model.Matrix[0].IsPrioritised);
        Assert.False(model.Matrix[2].IsPrioritised);
    }

    /// <summary>
    /// The guard against "a dimmed cell that just has lower opacity". A resting cell must carry both
    /// the resting attribute and a readable reopen time, and the sentence must name it so the state is
    /// announced rather than only coloured. The wrong input is a cell dimmed with no time text.
    /// </summary>
    [Fact]
    public void A_resting_cell_is_marked_and_names_the_local_time_it_reopens()
    {
        var reopens = DateTimeOffset.UtcNow.AddHours(6);
        var model = Model(Cell(1, "R-DET", "B2", PracticeCellMasteryDto.Mastered,
            prioritised: true, reopens: reopens));

        var row = Assert.Single(model.Matrix);
        Assert.True(row.IsResting);
        Assert.Contains("is-resting", row.CssClass);
        Assert.Contains("is-prioritised", row.CssClass);
        Assert.Equal("Reopens 09:00 05 Nov", row.StatusLine);
        Assert.Contains("resting", row.AccessibleLabel);
        Assert.Contains("09:00 05 Nov", row.AccessibleLabel);
        Assert.Contains("recommended this week", row.AccessibleLabel);
    }

    /// <summary>
    /// The reopen instant is formatted by the controller in the learner's zone. This asserts the
    /// conversion is the formatter's, not a UTC print: a UTC+7 learner's 02:00Z is 09:00 local, and the
    /// wrong input that would pass is a view printing `02:00`.
    /// </summary>
    [Fact]
    public void The_reopen_time_is_rendered_in_the_learners_zone_not_utc()
    {
        var instant = new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero);

        Assert.Equal("09:00 05 Nov",
            WebMVC.Formatters.DateTimeFormatter.FormatInZone(instant, "Asia/Ho_Chi_Minh", "HH:mm dd MMM"));
    }

    [Fact]
    public void An_open_cell_says_it_is_open_now_and_carries_no_resting_class()
    {
        var model = Model(Cell(1, "R-DET", "B2", PracticeCellMasteryDto.Learning));

        var row = Assert.Single(model.Matrix);
        Assert.False(row.IsResting);
        Assert.DoesNotContain("is-resting", row.CssClass);
        Assert.Equal("Open now", row.StatusLine);
    }

    [Fact]
    public void A_catalog_with_no_matrix_renders_no_matrix_section()
    {
        var model = new PracticeCatalogViewModel(EmptyCatalog());

        Assert.False(model.HasMatrix);
        Assert.Empty(model.Matrix);
        Assert.Equal(0, model.PrioritisedCellCount);
    }
}
