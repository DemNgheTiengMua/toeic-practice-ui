using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #202 / S-01: the coverage view, read through the service against the real schema.
/// <para>
/// The schema test <c>domain29-coverage.sql</c> proves the view's arithmetic in T-SQL. These prove the
/// two things it cannot: that EF reads the keyless view at all, and that the service adds no number of
/// its own. Every assertion below is a column of the view, so a service that recomputed a deficit would
/// disagree with the quota it is handed.
/// </para>
/// <para>
/// Each of these runs on its own database because the fixture's is shared with 60 other classes, and an
/// assertion about "how many cells are non-zero" is only meaningful on a pool nobody else wrote.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminCoverageServiceTests(DatabaseFixture database)
{
    private static readonly string[] CanonicalTopics =
    [
        "L-GIST", "L-DET", "L-INF", "L-ATT", "L-FUNC", "L-ORG",
        "R-GIST", "R-DET", "R-INF", "R-ATT", "R-COH", "R-GRAM",
        "S-DESC", "S-INT", "S-ARG", "S-SYN",
        "W-CORR", "W-DATA", "W-ARG", "W-DISC",
    ];

    /// <summary>A fresh database with the canonical twenty topics and nothing authored.</summary>
    private async Task<(string ConnectionString, string DatabaseName)> FreshAsync()
    {
        var name = $"EECoverage_{Guid.NewGuid():N}"[..26];
        var connectionString = await database.CreateFreshDatabaseAsync(name);

        await using var db = DbContextFactory.Create(connectionString);
        foreach (var code in CanonicalTopics)
        {
            var modality = code[0] switch
            {
                'L' => "listening",
                'R' => "reading",
                'S' => "speaking",
                _ => "writing",
            };
            db.Topics.Add(new BusinessObjects.Entities.Topic { Modality = modality, Code = code, Name = code });
        }

        await db.SaveChangesAsync();
        return (connectionString, name);
    }

    [Fact]
    public async Task The_matrix_is_120_cells_with_the_per_skill_split_the_ticket_names()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var matrix = await new AdminCoverageService(db).GetMatrixAsync();

            Assert.Equal(120, matrix.Cells.Count);

            // 120 is the total, not the per-skill count. A view that produced 120 per skill would be
            // 480 rows and would still "hold 120 cells" under a careless assertion.
            Assert.Equal(36, matrix.Cells.Count(c => c.Modality == "listening"));
            Assert.Equal(36, matrix.Cells.Count(c => c.Modality == "reading"));
            Assert.Equal(24, matrix.Cells.Count(c => c.Modality == "speaking"));
            Assert.Equal(24, matrix.Cells.Count(c => c.Modality == "writing"));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The ticket's fourth criterion below the view: a cell with zero content is a row carrying the
    /// full deficit, not a missing row. Every cell here is empty, so a group-over-items view would
    /// return 120 fewer rows and this test would fail on the count rather than on a blank plaque.
    /// </summary>
    [Fact]
    public async Task An_empty_cell_is_a_row_with_a_deficit_and_not_an_absence()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var matrix = await new AdminCoverageService(db).GetMatrixAsync();

            Assert.All(matrix.Cells, cell =>
            {
                Assert.Equal(0, cell.ActiveItemCount);
                Assert.Equal(0, cell.PlacementEligibleItemCount);
                Assert.True(cell.TargetItemCount > 0, "a cell always has a budget");
                Assert.Equal(cell.TargetItemCount, cell.Deficit);
            });

            var cell = Assert.Single(matrix.Cells, c => c.TopicCode == "R-DET" && c.CefrLevel == "A2");
            Assert.Equal(30, cell.TargetItemCount);
            Assert.Equal(30, cell.Deficit);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The aggregation trap. Four items are authored in one cell: two active, one retired, one draft —
    /// and the retired one is placement-eligible, so a view counting <c>PlacementEligible</c> without
    /// the lifecycle filter reports 2 instead of 1, and one counting every row reports 4.
    /// </summary>
    [Fact]
    public async Task Only_active_items_count_and_the_deficit_is_the_target_less_that_count()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var topic = db.Topics.Single(t => t.Code == "R-DET");

            db.Items.AddRange(
                Item(topic.TopicId, "active", placementEligible: true),
                Item(topic.TopicId, "active", placementEligible: false),
                Item(topic.TopicId, "retired", placementEligible: true),
                Item(topic.TopicId, "draft", placementEligible: true));
            await db.SaveChangesAsync();

            var matrix = await new AdminCoverageService(db).GetMatrixAsync();
            var cell = Assert.Single(matrix.Cells, c => c.TopicCode == "R-DET" && c.CefrLevel == "B1");

            Assert.Equal(2, cell.ActiveItemCount);
            Assert.Equal(1, cell.PlacementEligibleItemCount);
            Assert.Equal(cell.TargetItemCount - 2, cell.Deficit);

            // ...and no other cell moved
            Assert.Single(matrix.Cells, c => c.ActiveItemCount != 0);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The quota is data. Shrinking one row changes that cell's deficit and no other, which is what a
    /// C# constant or a CHECK could not do — and it is why the service must read the target rather
    /// than assume 30.
    /// </summary>
    private static BusinessObjects.Entities.Item Item(int topicId, string lifecycle, bool placementEligible) => new()
    {
        Modality = "reading",
        TopicId = topicId,
        CefrLevel = BusinessObjects.CefrLevel.B1,
        ItemType = "passage_comprehension",
        PlacementEligible = placementEligible,
        ContentTier = "production",
        LifecycleStatus = lifecycle,
    };
}
