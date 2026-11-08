using BusinessObjects;
using Contracts;
using WebAPI.Endpoints;

namespace WebAPI.Tests;

/// <summary>
/// #238 / SCR-PRAC-01: which matrix cells the hub highlights.
/// <para>
/// The plan's top-three is a <b>set of coordinates</b>, not "the first three rows". The wrong input that
/// would pass a weaker test is a fixture whose priority order and row order agree — so here the three
/// prioritised cells sit at positions 2, 4 and 5, and row 1 is not prioritised.
/// </para>
/// </summary>
public sealed class PracticeMatrixPriorityTests
{
    private static PracticeCellDto Cell(int topicId, string level) =>
        new(topicId, $"T{topicId}", $"Topic {topicId}", ModalityDto.Reading, level,
            PracticeCellMasteryDto.Untested, IsPrioritised: false, ReopensAt: null);

    private static PrioritisedCoordinate Coordinate(int topicId, CefrLevel level) =>
        new(1, "READ", "Reading", Modality.Reading, topicId, $"T{topicId}", $"Topic {topicId}", level, "week 1");

    [Fact]
    public void The_plans_coordinates_are_highlighted_by_identity_not_by_row_position()
    {
        var cells = new[]
        {
            Cell(1, "A2"),
            Cell(2, "B1"),
            Cell(3, "B1"),
            Cell(4, "B2"),
            Cell(5, "B2"),
        };

        var priorities = new[]
        {
            Coordinate(2, CefrLevel.B1),
            Coordinate(4, CefrLevel.B2),
            Coordinate(5, CefrLevel.B2),
        };

        var result = PracticeEndpoints.MarkPrioritised(cells, priorities);

        Assert.Equal(
            [2, 4, 5],
            result.Where(c => c.IsPrioritised).Select(c => c.TopicId));
        Assert.False(result[0].IsPrioritised);
        Assert.False(result[2].IsPrioritised);
    }

    /// <summary>
    /// A coordinate is a topic <b>at a level</b>: the same topic at another level is a different cell and
    /// must not be highlighted. The wrong input is a match on topic id alone.
    /// </summary>
    [Fact]
    public void A_different_level_of_a_prioritised_topic_is_not_highlighted()
    {
        var cells = new[] { Cell(5, "B1"), Cell(5, "B2") };
        var priorities = new[] { Coordinate(5, CefrLevel.B2) };

        var result = PracticeEndpoints.MarkPrioritised(cells, priorities);

        Assert.False(result.Single(c => c.LevelCode == "B1").IsPrioritised);
        Assert.True(result.Single(c => c.LevelCode == "B2").IsPrioritised);
    }

    [Fact]
    public void No_priorities_means_no_cell_is_highlighted()
    {
        var cells = new[] { Cell(1, "A2"), Cell(2, "B1") };

        var result = PracticeEndpoints.MarkPrioritised(cells, []);

        Assert.All(result, c => Assert.False(c.IsPrioritised));
    }
}
