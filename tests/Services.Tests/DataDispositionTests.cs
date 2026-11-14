using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #246.3, #246.4, #246.5: <b>what survives a cutover is decided once, for every table, and the
/// decision can fail.</b>
/// <para>
/// <b>Why the list is code.</b> A disposition table in a document cannot fail, so a new table arrives
/// unclassified and nobody notices until the cutover drops something it should have kept. These tests
/// read the schema and require every table to be named exactly once - so adding a table without deciding
/// its fate is a red suite, not an oversight found on the night.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> A scan that found no tables would pass an
/// "every table is classified" assertion vacuously. The scan asserts it read the schema, and the
/// disposition list asserts a minimum size; both numbers are checked below.
/// </para>
/// </summary>
public sealed class DataDispositionTests
{
    /// <summary>Every table the schema creates, read from the single source of truth (ADR-0001).</summary>
    private static IReadOnlyList<string> SchemaTables()
    {
        var path = Path.Combine(RepoRoot(), "docs", "database-schema.sql");
        var tables = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("CREATE TABLE ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = trimmed["CREATE TABLE ".Length..];
            var name = new string(rest.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (name.Length > 0)
            {
                tables.Add(name);
            }
        }

        return tables;
    }

    /// <summary>
    /// Every table has a fate. A table nobody classified is the failure this criterion exists to prevent:
    /// a cutover script cannot preserve what it does not know about, and cannot honestly drop it either.
    /// </summary>
    [Fact]
    public void Every_table_in_the_schema_has_exactly_one_disposition()
    {
        var tables = SchemaTables();

        // The scan must have read the schema: a wrong path or a changed DDL shape would find nothing and
        // pass every assertion below without touching anything.
        Assert.True(tables.Count > 40, $"the disposition scan found {tables.Count} table(s); it read the wrong file");

        var unclassified = tables
            .Where(t => DataDisposition.For(t) is null)
            .ToList();

        Assert.True(unclassified.Count == 0,
            "these tables have no cutover disposition, so nobody has decided whether they survive: "
            + string.Join(", ", unclassified));
    }

    /// <summary>
    /// And no phantom entries: a disposition for a table the schema does not have is a rule about
    /// something that no longer exists, which reads as covered and is not.
    /// </summary>
    [Fact]
    public void No_disposition_names_a_table_the_schema_does_not_have()
    {
        var tables = SchemaTables().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var phantom = DataDisposition.Entries
            .Where(e => !tables.Contains(e.Table))
            .Select(e => e.Table)
            .ToList();

        Assert.Empty(phantom);
    }

    /// <summary>
    /// The three lists the ticket names, by content rather than by count - because a count cannot tell a
    /// rename from a removal.
    /// </summary>
    [Fact]
    public void The_preserved_dropped_and_rebuilt_sets_match_the_ticket()
    {
        // #246.3: "Users, entitlements, the certificate catalogue and band tables, and mentor bookings
        // survive and are augmented rather than replaced."
        foreach (var table in new[]
                 {
                     "Users", "Roles", "Permissions", "RolePermissions", "CreditTransactions",
                     "Orders", "PaymentEvents", "Certificates", "Skills", "ScoreBands", "ScoreScales",
                     "MentorProfiles", "MentorWeeklySlots", "MentorSlots", "Bookings",
                 })
        {
            Assert.Equal(Disposition.Preserve, DataDisposition.For(table)!.Disposition);
        }

        // #246.2: completed mock attempts are preserved (with their answers and gradings).
        foreach (var table in new[]
                 {
                     "ExamAttempts", "AttemptAnswers", "AttemptSkillScores", "FreeResponses",
                     "AiGradings", "AiGradingScores",
                 })
        {
            Assert.Equal(Disposition.Preserve, DataDisposition.For(table)!.Disposition);
        }

        // #246.4: "Placement results, topic mastery, learning paths, insights, legacy items, topics,
        // lessons and rubrics are dropped rather than migrated."
        foreach (var table in new[]
                 {
                     "PlacementTests",
                     "UserCategoryMasteries", "LearnerPaths", "LearnerPathSteps",
                     "AiInsights", "Recommendations", "Items", "ItemVersions", "Stimuli", "Topics",
                     "Lessons", "LessonCards", "GradingCriteria", "SectionCriteria",
                 })
        {
            Assert.Equal(Disposition.Drop, DataDisposition.For(table)!.Disposition);
        }

        // #269, #307 & #314: tables that carried the legacy content model, retired mentor payouts,
        // or retired pool/gate/audit/two-stage/cell-mastery/sliding-window/single-writer artifacts are GONE
        // from the schema, so the list must not classify them any more - a disposition describes a table
        // that exists. Asserted rather than merely deleted, so re-adding one is a failure instead of a
        // silent resurrection.
        foreach (var gone in new[]
                 {
                     "Questions", "QuestionGroups", "QuestionOptions", "UserQuestionHistories",
                     "MentorPayouts", "MentorPayoutItems",
                     "DerivedFactOwners", "ReviewStrata", "ReviewSampleItems", "ReviewSampleDecisions",
                     "ItemTelemetryEvaluations", "ContentQuotas", "AttemptStageBundles", "AttemptItemResponses",
                     "CefrCutScores", "CefrCutScoreVersions", "LearnerSkillStates", "PlacementSkillResults",
                     "UserCellMasteries", "CalibrationDecisions", "LearnerPathWeeks", "ExamEditions",
                     "ExamPublishAudits", "GradingRubricLocks", "GradingCriterionDescriptors", "GradingCriterionAnchors",
                 })
        {
            Assert.Null(DataDisposition.For(gone));
        }

        // #246.5: legacy saved links are dropped.
        Assert.Equal(Disposition.Drop, DataDisposition.For("SavedQuestions")!.Disposition);
    }

    /// <summary>Every entry states why. "Drop" with no reason is indistinguishable from an oversight.</summary>
    [Fact]
    public void Every_disposition_carries_a_reason()
    {
        Assert.All(DataDisposition.Entries, entry =>
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason), entry.Table));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExtraEfficiency.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
