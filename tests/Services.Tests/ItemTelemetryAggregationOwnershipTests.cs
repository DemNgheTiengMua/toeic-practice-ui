using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #251: the certificate's aggregation rule has one owner, and telemetry is not it.
/// <para>
/// The defect these tests exist for: <c>ItemTelemetryService.AggregateTotal</c> re-derived the rule and
/// its default arm <em>summed</em> anything it did not recognise, while
/// <c>AttemptEngine.AggregateTotalScore</c> refused the same value. Two answers to one question, and
/// the one nobody watched was the wrong one. A certificate whose rule is neither <c>sum</c>,
/// <c>avg_half_step</c> nor <c>avg_whole</c> is the input that splits them.
/// </para>
/// <para>
/// <b>Every test here authors a whole certificate — skills, sections, bands, scales, a paper, items,
/// versions and a learner — so every test runs on its own disposable database.</b> The collection
/// shares one catalog across the assembly; seeding a certificate and a user into it would leak into
/// every other class, and the symptom is not a failure here but a duplicate-email failure three
/// classes away. <c>CreateFreshDatabaseAsync</c> and <c>DropDatabaseAsync</c> are the pattern, and a
/// <c>[Theory]</c> gets one database per row for the same reason.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemTelemetryAggregationOwnershipTests(DatabaseFixture database)
{
    /// <summary>
    /// Two reading skills on one certificate, so the three rules produce three different totals:
    /// skill one scores 60 and skill two scores 65. Their average is 62.5, the only average on which
    /// <c>avg_whole</c> (63, away from zero) and <c>avg_half_step</c> (65) disagree, and <c>sum</c>
    /// is 125 — so an implementation that always summed, or always half-stepped, is caught.
    /// </summary>
    private sealed record RuleFixture(
        int CertId, int ExamId, int LearnerId, int DefectiveItemId,
        int[] QuestionIds, int[] VersionIds);

    private sealed record Sitting(int AttemptId, int? Total, string? Band, string? Writer, int Version);

    /// <summary>A name short enough for the catalog, unique enough that two runs cannot collide.</summary>
    private static string FreshName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..25];

    private static async Task RunSqlAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The service over the database the fixture was seeded into. It takes the connection string
    /// rather than reading the fixture's, so a test cannot seed one database and then assert against
    /// another — which would pass while proving nothing.
    /// </summary>
    private static ItemTelemetryService CreateService(string connectionString) =>
        new(DbContextFactory.Create(connectionString), TimeProvider.System);

    /// <summary>
    /// One certificate, two reading skills, a three-position paper and one item per position. The
    /// defective item's stored key is D; the sound ones are keyed A.
    /// <para>
    /// <paramref name="storedRule"/> must be one of the three the schema names unless
    /// <paramref name="allowUnstorableRule"/> is set — that one disables
    /// <c>CK_Certificates_ScoreAggregationRule</c> so a pre-#171 row can be reproduced, and it leaves
    /// the constraint disabled for the life of that database, so it is only ever used on a disposable
    /// one.
    /// </para>
    /// </summary>
    private static async Task<RuleFixture> SeedRuleFixtureAsync(
        string connectionString, string storedRule, bool allowUnstorableRule = false)
    {
        var certCode = $"TR{Guid.NewGuid():N}"[..16];
        var tag = $"own{Guid.NewGuid():N}"[..18];
        var email = $"{Guid.NewGuid():N}@test.local";

        var ruleAssignment = allowUnstorableRule
            ? $"""
              ALTER TABLE Certificates NOCHECK CONSTRAINT CK_Certificates_ScoreAggregationRule;
              UPDATE Certificates SET ScoreAggregationRule = '{storedRule}' WHERE CertId = @cert;
              """
            : $"UPDATE Certificates SET ScoreAggregationRule = '{storedRule}' WHERE CertId = @cert;";

        await RunSqlAsync(connectionString, $"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-COH')
              INSERT INTO Topics (Modality, Code, Name)
              VALUES ('reading', 'R-COH', N'Discourse cohesion, reference, text insertion');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name)
              VALUES ('reading', 'R-DET', N'Detail and specific information');

            DECLARE @t1 INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-COH');
            DECLARE @t2 INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('{certCode}', N'Ownership cert', 1, 'sum');
            DECLARE @cert INT = SCOPE_IDENTITY();

            -- #257: the rule is set BEFORE the paper is inserted, because publishing freezes the
            -- certificate's rule onto the edition and scoring now reads the edition. Setting it after
            -- the insert would make the edition say 'sum' while this fixture believes it says {storedRule}
            -- - the exact drift the ticket exists to prevent, reproduced inside a test.
            {ruleAssignment}

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'S1', N'Skill one', 'reading', 1, 0, 100, 1),
              (@cert, 'S2', N'Skill two', 'reading', 2, 0, 100, 1);
            DECLARE @s1 INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'S1');
            DECLARE @s2 INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'S2');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@s1, @cert, 'reading', 'P1', N'Part one', 4, 2, 1),
              (@s2, @cert, 'reading', 'P2', N'Part two', 4, 1, 2);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P2');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 129, 1), (@cert, 'HIGH', N'High', 130, 990, 2);

            -- skill one: raw 1 of 2 interpolates to 60; skill two: raw 1 of 1 is 65
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @s1, NULL, 0, 40), (@cert, @s1, NULL, 2, 80),
              (@cert, @s2, NULL, 0, 50), (@cert, @s2, NULL, 1, 65);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Ownership paper', 'published', 60, 'mock', 3);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @t1, 'B1', 'sentence_cloze', 'demo', 'active', '{tag}-bad');
            DECLARE @i1 INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@i1, 1, N'Defective stem', N'a', N'b', N'c', N'd', 'D', N'The stored key is wrong; the true key is A or C.');
            DECLARE @v1 INT = SCOPE_IDENTITY();

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @t1, 'B1', 'sentence_cloze', 'demo', 'active', '{tag}-ok1');
            DECLARE @i2 INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@i2, 1, N'Sound stem one', N'a', N'b', N'c', N'd', 'A', N'Why one.');
            DECLARE @v2 INT = SCOPE_IDENTITY();

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @t2, 'B1', 'sentence_cloze', 'demo', 'active', '{tag}-ok2');
            DECLARE @i3 INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@i3, 1, N'Sound stem two', N'a', N'b', N'c', N'd', 'A', N'Why two.');
            DECLARE @v3 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES
              (@exam, @cert, @sec1, 1), (@exam, @cert, @sec2, 2);
            DECLARE @g1 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @sec1);
            DECLARE @g2 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @sec2);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder) VALUES
              (@g1, @exam, @sec1, @s1, N'Position one', 'B1', 'mcq', 1),
              (@g1, @exam, @sec1, @s1, N'Position two', 'B1', 'mcq', 2),
              (@g2, @exam, @sec2, @s2, N'Position three', 'B1', 'mcq', 1);

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
              (@exam, @sec1, @cert, @v1, @i1, 'reading', 1),
              (@exam, @sec1, @cert, @v2, @i2, 'reading', 2),
              (@exam, @sec2, @cert, @v3, @i3, 'reading', 1);

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Ownership learner', @cert);

            {ruleAssignment}
            """);

        await using var db = DbContextFactory.Create(connectionString);
        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var versions = await db.ItemVersions
            .Where(v => v.Item.BatchTag!.StartsWith(tag))
            .Select(v => new { v.ItemId, v.ItemVersionId, BatchTag = v.Item.BatchTag! })
            .ToListAsync();
        var questions = await db.Questions
            .Where(q => q.Section.CertId == certId)
            .OrderBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => q.QuestionId)
            .ToListAsync();

        return new RuleFixture(
            certId,
            await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync(),
            await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync(),
            versions.Single(v => v.BatchTag == $"{tag}-bad").ItemId,
            questions.ToArray(),
            versions.OrderBy(v => v.BatchTag, StringComparer.Ordinal).Select(v => v.ItemVersionId).ToArray());
    }

    /// <summary>
    /// Sits the whole paper: position one answered <paramref name="defectiveLabel"/> (the defective
    /// item's stored key is D), positions two and three answered A (their key). Returns what the
    /// sitting engine stored, read back from the row rather than from its return value.
    /// </summary>
    private static async Task<Sitting> SitAsync(
        string connectionString, RuleFixture fixture, string defectiveLabel = "B")
    {
        int attemptId;
        await using (var ctx = DbContextFactory.Create(connectionString))
        {
            var engine = new AttemptEngine(ctx);
            attemptId = await SitUnscoredAsync(engine, fixture, defectiveLabel);
            await engine.SubmitAttemptAsync(attemptId, fixture.LearnerId, isAutoSubmit: false);
        }

        await using var db = DbContextFactory.Create(connectionString);
        var stored = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        return new Sitting(attemptId, stored.TotalScore, stored.BandCode, stored.WrittenByService, stored.ProjectionVersion);
    }

    /// <summary>Creates an attempt and answers all three positions, without submitting it.</summary>
    private static async Task<int> SitUnscoredAsync(AttemptEngine engine, RuleFixture fixture, string defectiveLabel)
    {
        var attempt = await engine.CreateAttemptAsync(fixture.LearnerId, fixture.CertId, fixture.ExamId, 60);
        var index = Array.IndexOf(["A", "B", "C", "D"], defectiveLabel);

        await engine.SaveAnswerAsync(attempt.AttemptId, fixture.LearnerId, fixture.QuestionIds[0], fixture.VersionIds[0] * 10 + index + 1, false);
        await engine.SaveAnswerAsync(attempt.AttemptId, fixture.LearnerId, fixture.QuestionIds[1], fixture.VersionIds[1] * 10 + 1, false);
        await engine.TransitionToNextSkillAsync(attempt.AttemptId, fixture.LearnerId);
        await engine.SaveAnswerAsync(attempt.AttemptId, fixture.LearnerId, fixture.QuestionIds[2], fixture.VersionIds[2] * 10 + 1, false);
        return attempt.AttemptId;
    }

    // =============================================================================================
    // AC2: the same attempt, the same total, whichever of the three real rules the certificate names.
    // =============================================================================================

    /// <summary>
    /// <paramref name="expectedTotal"/> is an oracle outside both implementations: it is what the
    /// certificate's rule says two skill scores of 60 and 65 become. An implementation that always
    /// summed would report 125 for every case; one that always half-stepped would report 65 for
    /// <c>avg_whole</c>. The engine's stored total and the recalculation are asserted against that
    /// number, and against each other, so neither can pass by agreeing with itself.
    /// <para>
    /// The learner's choice (B) is not the true key (C), so crediting the true key leaves both raw
    /// scores exactly where the sitting engine left them: the recalculation is a second opinion on one
    /// number, not a rescore of different answers.
    /// </para>
    /// <para>One database per row, dropped in <c>finally</c>, for the reason in the class summary.</para>
    /// </summary>
    [Theory]
    [InlineData("sum", 125)]
    [InlineData("avg_whole", 63)]
    [InlineData("avg_half_step", 65)]
    public async Task Telemetry_and_the_sitting_engine_report_the_same_total_for_each_real_rule(
        string rule, int expectedTotal)
    {
        var databaseName = FreshName("EE251Rule");
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            var fixture = await SeedRuleFixtureAsync(connectionString, rule);

            var sitting = await SitAsync(connectionString, fixture);
            Assert.Equal(expectedTotal, sitting.Total);
            Assert.Equal("LOW", sitting.Band);

            var recalculation = await CreateService(connectionString)
                .RecalculateForDefectiveItemAsync(fixture.DefectiveItemId, trueKeyLabel: "C");

            var outcome = Assert.Single(recalculation.Attempts);
            Assert.Equal(sitting.AttemptId, outcome.AttemptId);
            Assert.Equal(expectedTotal, outcome.PreviousTotal);
            Assert.Equal(expectedTotal, outcome.RecalculatedTotal);
            // An equal band is not an upgrade, so nothing is written and the learner's row stands as
            // the engine left it.
            Assert.Equal("kept", outcome.Outcome);

            await using var db = DbContextFactory.Create(connectionString);
            var stored = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == sitting.AttemptId);
            Assert.Equal(expectedTotal, stored.TotalScore);
            Assert.Equal(sitting.Version, stored.ProjectionVersion);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    // =============================================================================================
    // AC3: a rule neither method recognises is refused by both, never summed by either.
    // =============================================================================================

    /// <summary>
    /// The rule is <c>independent</c>, which <c>CK_Certificates_ScoreAggregationRule</c> no longer
    /// names: a database that predates #171 still holds it, which is exactly the input that made the
    /// two implementations disagree. The fixture disables that CHECK to author the row, so this runs
    /// on a disposable database.
    /// <para>
    /// The refusal must be <c>aggregation_rule_unknown</c> naming the rule and not merely "an
    /// exception": a missing scale or a missing band would refuse for the wrong reason. The stored
    /// total must also stay untouched — a silent sum is the defect, so an implementation that returned
    /// 125 instead of throwing is caught by the <c>ThrowsAsync</c>, and one that threw but wrote first
    /// is caught by the row reading 0 afterwards.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_rule_the_sitting_engine_refuses_is_refused_by_telemetry_and_never_summed()
    {
        var databaseName = FreshName("EE251Refuse");
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            var fixture = await SeedRuleFixtureAsync(connectionString, "independent", allowUnstorableRule: true);

            // 1. The sitting engine refuses to score an attempt on this certificate.
            await using (var ctx = DbContextFactory.Create(connectionString))
            {
                var engine = new AttemptEngine(ctx);
                var attemptId = await SitUnscoredAsync(engine, fixture, "B");

                var refused = await Assert.ThrowsAsync<ScoreConversionException>(
                    () => engine.SubmitAttemptAsync(attemptId, fixture.LearnerId, isAutoSubmit: false));
                Assert.Equal(ScoreConversionException.AggregationRuleUnknown, refused.Code);
                Assert.Contains("independent", refused.Message, StringComparison.Ordinal);

                // Neither a zero nor a sum was invented for the attempt.
                var untouched = await ctx.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
                Assert.Null(untouched.TotalScore);
            }

            // 2. Telemetry refuses to rescore an attempt on the same certificate. The attempt is marked
            // graded with a wrong stored total (0) and its three answers are on record, so an
            // implementation that summed would report 125 and write it: the assertion is the refusal.
            int gradedAttemptId;
            await using (var ctx = DbContextFactory.Create(connectionString))
            {
                var engine = new AttemptEngine(ctx);
                gradedAttemptId = await SitUnscoredAsync(engine, fixture, "B");
            }

            await RunSqlAsync(connectionString, $"""
                UPDATE ExamAttempts
                SET Status = 'graded', TotalScore = 0, BandCode = 'LOW',
                    WrittenByService = 'AttemptEngine', ProjectionVersion = 1
                WHERE AttemptId = {gradedAttemptId};
                """);

            var telemetry = CreateService(connectionString);
            var refusedRecalculation = await Assert.ThrowsAsync<ScoreConversionException>(
                () => telemetry.RecalculateForDefectiveItemAsync(fixture.DefectiveItemId, trueKeyLabel: "C"));
            Assert.Equal(ScoreConversionException.AggregationRuleUnknown, refusedRecalculation.Code);
            Assert.Contains("independent", refusedRecalculation.Message, StringComparison.Ordinal);

            await using var check = DbContextFactory.Create(connectionString);
            var stillWrong = await check.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == gradedAttemptId);
            Assert.Equal(0, stillWrong.TotalScore);
            Assert.Equal("LOW", stillWrong.BandCode);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    // =============================================================================================
    // AC4: the write declares the owner the registry names, and the trigger is what accepts it.
    // =============================================================================================

    /// <summary>
    /// The learner chose A on the defective item, whose stored key is D and whose true key is A: the
    /// recalculation credits the choice, skill one goes from 1 of 2 to 2 of 2, and the total rises from
    /// 125 (LOW) to 145 (HIGH). That is a recalculation that writes, the only kind whose declaration
    /// can be judged.
    /// <para>
    /// Reading <c>WrittenByService</c> off the row proves nothing by itself — a name in a column is not
    /// a guard. What makes it one is the contrast at the end: the same row declaring any other service
    /// is refused by <c>trg_Attempts_SingleWriter</c> with 50070, so the accepted write is accepted
    /// <em>because</em> it declared the registered owner. That control is also what keeps "register
    /// telemetry as a second owner" out: under that change the control write would be accepted.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_recalculated_score_is_written_by_the_registered_owner_and_the_trigger_refuses_any_other()
    {
        var databaseName = FreshName("EE251Writer");
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            var fixture = await SeedRuleFixtureAsync(connectionString, "sum");

            var sitting = await SitAsync(connectionString, fixture, defectiveLabel: "A");
            Assert.Equal(125, sitting.Total);
            Assert.Equal("LOW", sitting.Band);

            await using var db = DbContextFactory.Create(connectionString);
            var owner = await db.Database
                .SqlQueryRaw<string>("SELECT OwningService AS Value FROM DerivedFactOwners WHERE FactCode = 'attempt_score_and_band'")
                .SingleAsync();
            // Hard-coded on purpose: a registry that named telemetry would make every other assertion
            // here pass while the fact gained a second writer.
            Assert.Equal("AttemptEngine", owner);
            Assert.Equal(owner, sitting.Writer);

            var recalculation = await CreateService(connectionString)
                .RecalculateForDefectiveItemAsync(fixture.DefectiveItemId, trueKeyLabel: "A");

            var outcome = Assert.Single(recalculation.Attempts);
            Assert.Equal("upgraded", outcome.Outcome);
            Assert.Equal(145, outcome.RecalculatedTotal);

            var stored = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == sitting.AttemptId);
            Assert.Equal(145, stored.TotalScore);
            Assert.Equal("HIGH", stored.BandCode);
            // The recalculation's write declares the same single name as the engine's own scoring write,
            // and lands as a newer projection — the two halves the trigger requires.
            Assert.Equal(owner, stored.WrittenByService);
            Assert.True(stored.ProjectionVersion > sitting.Version);

            // Contrast: the same row, declaring a second writer's name, is refused by the trigger.
            var refused = await Assert.ThrowsAsync<SqlException>(() => RunSqlAsync(
                connectionString,
                $"UPDATE ExamAttempts SET WrittenByService = 'ItemTelemetryService', ProjectionVersion = ProjectionVersion + 1 WHERE AttemptId = {sitting.AttemptId};"));
            Assert.Equal(50070, refused.Number);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
