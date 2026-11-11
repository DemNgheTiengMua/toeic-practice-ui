using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269 criterion 2: <b>the legacy content model is absent from the schema, and the lookup that says so is
/// proved to be looking at the real catalogue.</b>
/// <para>
/// <b>Why this is a guarded test rather than six asserts.</b> A query against <c>sys.tables</c> that is
/// pointed at the wrong catalogue - a stale connection, a database that was never created, a typo in the
/// role - returns <i>zero rows</i> and every "this table is absent" assertion passes. The test would be
/// green on a machine where nothing had been dropped because nothing had been created either. So the file
/// first proves the lookup WORKS by finding an object that must exist, then asserts the removals.
/// </para>
/// <para>
/// <b>Why absence rather than an empty count.</b> <c>SELECT COUNT(*) FROM Questions = 0</c> passes on a
/// table that still exists, and the whole point of the ticket is that the table is gone: an empty table is
/// still a table other code can be written against.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LegacyContentModelIsGoneTests(DatabaseFixture database)
{
    /// <summary>The objects #269 removes. Each is named once, here.</summary>
    private static readonly string[] RemovedTables =
    [
        "Questions",
        "QuestionGroups",
        "QuestionOptions",
        "UserQuestionHistories",
    ];

    private static readonly string[] RemovedViews = ["vw_UserCategoryAccuracy"];

    [Fact]
    public async Task The_schema_catalogue_answers_queries_at_all()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // The control. Without it every assertion below would pass vacuously on an empty catalogue.
        var known = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.tables WHERE name = {"Items"}")
            .SingleAsync();

        Assert.Equal(1, known);

        var knownView = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.views WHERE name = {"vw_UserTopicAccuracy"}")
            .SingleAsync();

        Assert.Equal(1, knownView);
    }

    [Fact]
    public async Task The_legacy_content_tables_are_absent()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        foreach (var table in RemovedTables)
        {
            var present = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.tables WHERE name = {table}")
                .SingleAsync();

            Assert.True(present == 0,
                $"{table} still exists in the schema. #269 removes it; a table that still exists can still be "
                + "written against, so the content model is not gone.");
        }
    }

    [Fact]
    public async Task The_retired_category_view_is_absent()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        foreach (var view in RemovedViews)
        {
            var present = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.views WHERE name = {view}")
                .SingleAsync();

            Assert.True(present == 0,
                $"{view} still exists. #271 replaced it with vw_UserTopicAccuracy and #269 removes it.");
        }
    }

    /// <summary>
    /// The other half of "gone": nothing in the schema may still point at a removed table. A view or a
    /// trigger that references <c>Questions</c> would make the DROP fail, and a foreign key the other way
    /// would make it fail harder - this names the referrer rather than letting SQL Server say
    /// "could not drop object".
    /// </summary>
    [Fact]
    public async Task No_schema_object_still_references_a_removed_table()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var referrers = await db.Database
            .SqlQuery<string>($"""
                SELECT DISTINCT OBJECT_NAME(referencing_id) AS Value
                FROM sys.sql_expression_dependencies
                WHERE referenced_id IN (SELECT object_id FROM sys.tables WHERE name = {"Questions"})
                   OR referenced_id IN (SELECT object_id FROM sys.tables WHERE name = {"QuestionGroups"})
                   OR referenced_id IN (SELECT object_id FROM sys.tables WHERE name = {"QuestionOptions"})
                """)
            .ToListAsync();

        Assert.True(referrers.Count == 0,
            "These schema objects still reference a removed content table, so the drop would fail: "
            + string.Join(", ", referrers));
    }
}
