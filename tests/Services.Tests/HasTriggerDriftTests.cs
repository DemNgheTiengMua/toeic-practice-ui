using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// ADR-0001: a table with a trigger must declare <c>HasTrigger</c> in its EF configuration, or every
/// save on that table fails on the <c>OUTPUT</c> clause — at runtime, in the one place the tests never
/// look.
/// <para>
/// <b>Why a separate test.</b> <c>CertificateServiceTests</c> counts the triggers in the database and
/// names the important ones. Neither assertion can see the EF side, so a trigger added to the schema
/// without its declaration passes both: the count is right, the name is right, and the first save
/// throws. That is exactly how <c>trg_ExamAttempts_EditionIsFixed</c> and
/// <c>trg_PlacementTests_CompletionIsEarned</c> shipped, and <c>trg_Exams_TriageCannotWrite</c> and
/// <c>trg_Items_TriageCannotWrite</c> had been missing since before this wave.
/// </para>
/// <para>
/// <b>The direction that matters is DB → model, not model → DB.</b> The database is the schema's
/// source of truth (ADR-0001), so a trigger there without a declaration is the defect. A declaration
/// with no trigger is harmless — EF only omits the <c>OUTPUT</c> clause.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HasTriggerDriftTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_trigger_in_the_database_is_declared_on_its_entity()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // What the schema actually created, grouped by the table it is attached to.
        var inDatabase = await db.Database
            .SqlQuery<TriggerOnTable>($"""
                SELECT tr.name AS TriggerName, OBJECT_NAME(tr.parent_id) AS TableName
                FROM sys.triggers tr
                WHERE tr.parent_class = 1
                ORDER BY TableName, TriggerName
                """)
            .ToListAsync();

        Assert.NotEmpty(inDatabase);

        // What EF declares. A trigger's database name is what the schema created.
        var declared = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredTriggers())
            .Select(t => t.GetDatabaseName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var undeclared = inDatabase
            .Where(t => !declared.Contains(t.TriggerName))
            .Select(t => $"{t.TriggerName} on {t.TableName}")
            .ToList();

        // The message names every offender, because a count would let one substitution pass.
        Assert.True(
            undeclared.Count == 0,
            "These triggers exist in the database but no entity declares HasTrigger(...), so every EF "
            + "save on the table will fail on the OUTPUT clause (ADR-0001):\n  "
            + string.Join("\n  ", undeclared));
    }

    /// <summary>Row shape for the <c>sys.triggers</c> query above.</summary>
    public sealed class TriggerOnTable
    {
        public string TriggerName { get; set; } = string.Empty;

        public string TableName { get; set; } = string.Empty;
    }
}
