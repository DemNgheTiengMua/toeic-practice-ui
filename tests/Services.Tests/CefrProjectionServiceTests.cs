using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #210 / D6 / LVL-13: a certificate's own scale stops pretending to be a level. Projection is a
/// versioned lookup per skill, then the certificate's own aggregation rule — never arithmetic on a
/// total (LVL-12).
/// <para>
/// Every guard here is written as the <b>wrong</b> input that would still pass a plausible
/// implementation, because the three ways a cut-score lookup goes wrong are all silent:
/// a nearest-row fallback (a plausible answer to a question the table does not answer), a
/// display-ceiling row emitted as a level (the exact claim §4.6's C2 row exists to prevent), and a
/// certificate whose bands carry no study-hour evidence getting a prorated number instead of
/// <c>cannot_judge</c>.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CefrProjectionServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private ICefrProjectionService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new CefrProjectionService(db);
    }

    /// <summary>
    /// A certificate whose bands carry real study-hour evidence (the TOEIC/IELTS/VSTEP shape), with
    /// one skill per code and a cut-score version 1. <paramref name="rows"/> is
    /// (skillIndex, level, kind, min, max).
    /// </summary>
    private async Task<(int certId, int[] skillIds)> SeedAsync(
        string certCode,
        string aggregationRule,
        bool bandsCarryStudyHours,
        string[] skillCodes,
        (int Skill, string Level, CutScoreRowKind Kind, int? Min, int Max)[] rows,
        int tableVersion = 1,
        bool reportsSkillsSeparately = false,
        string? source = null,
        bool publishesVersion = true)
    {
        var skills = string.Join(",", skillCodes.Select((code, i) =>
            $"(@c, '{code}', N'{code}', '{ModalityOf(code)}', {i + 1})"));

        var bands = string.Join(",", new[]
        {
            $"(@c, 'A1', N'A1', 0, 100, 1, {(bandsCarryStudyHours ? "100" : "NULL")})",
            $"(@c, 'B1', N'B1', 101, 200, 2, {(bandsCarryStudyHours ? "500" : "NULL")})",
        });

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
              VALUES ('{certCode}', N'Cert {certCode}', 1, '{aggregationRule}');
            DECLARE @c INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES {skills};
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours)
              VALUES {bands};
            {(publishesVersion
                ? $"INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt) " +
                  $"VALUES (@c, {tableVersion}, {(reportsSkillsSeparately ? 1 : 0)}, N'{source ?? "test study"}', '2024-01-01');"
                : string.Empty)}
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var seededSkills = await db.Skills.Where(s => s.CertId == cert.CertId)
            .OrderBy(s => s.DisplayOrder).ToListAsync();

        if (rows.Length > 0)
        {
            var cutRows = string.Join(",", rows.Select(r =>
                $"(@c, {tableVersion}, {seededSkills[r.Skill].SkillId}, '{r.Level}', '{r.Kind.ToCode()}', " +
                $"{(r.Min is int min ? min.ToString() : "NULL")}, {r.Max})"));
            await database.SqlAsync($"""
                DECLARE @c INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
                INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled)
                  VALUES {cutRows};
                """);
        }

        return (cert.CertId, seededSkills.Select(s => s.SkillId).ToArray());
    }

    private static string ModalityOf(string skillCode) => skillCode switch
    {
        "LIST" => "listening",
        "READ" => "reading",
        "SPEA" => "speaking",
        _ => "writing",
    };

    private static Dictionary<int, (CefrLevel Level, int PositionInBand)> Coordinates(
        int[] skillIds, params (int Skill, CefrLevel Level, int Position)[] coordinates) =>
        coordinates.ToDictionary(c => skillIds[c.Skill], c => (c.Level, c.Position));

    // -----------------------------------------------------------------------------------------
    // AC4: the defect the old label caused - one certificate, two skills, two different levels.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The old product called a TOEIC total of 785-940 "B2". §4.6 says 785 is the *sum* of two
    /// independent minimums (Listening 400 + Reading 385), and a candidate at Listening B1 / Reading
    /// B2 lands inside that band while being below B2 in Listening.
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> a projector that reads the certificate's
    /// <c>ScoreBands</c> by total would return one level — B2 — for this candidate, and this test
    /// would see two equal levels. The assertion that catches it is that the two per-skill levels
    /// <i>differ</i>; a total-score lookup cannot produce that.
    /// </para>
    /// </summary>
    [Fact]
    public async Task One_certificate_projects_two_skills_to_two_different_levels()
    {
        var certCode = Unique("PROJD");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST", "READ"],
            [
                (0, "A1", CutScoreRowKind.Cut, 5, 105),
                (0, "B1", CutScoreRowKind.Cut, 275, 395),
                (0, "B2", CutScoreRowKind.Cut, 400, 485),
                (1, "A1", CutScoreRowKind.Cut, 5, 110),
                (1, "B1", CutScoreRowKind.Cut, 275, 380),
                (1, "B2", CutScoreRowKind.Cut, 385, 450),
            ]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 50), (1, CefrLevel.B2, 40)));

        var listening = projection.Skills.Single(s => s.SkillId == skills[0]);
        var reading = projection.Skills.Single(s => s.SkillId == skills[1]);

        Assert.Equal(CefrLevel.B1, listening.AwardedLevel);
        Assert.Equal(CefrLevel.B2, reading.AwardedLevel);
        Assert.NotEqual(listening.AwardedLevel, reading.AwardedLevel);

        // B1 at 50% inside 275-395, B2 at 40% inside 385-450.
        Assert.Equal(335, listening.ProjectedScore);
        Assert.Equal(411, reading.ProjectedScore);

        // The sum rule's total is a score. It is not a level, and there is nowhere on it to put one.
        Assert.Equal(746, projection.CombinedTotal);
        Assert.Null(typeof(CertificateProjection).GetProperty("Level"));
        Assert.Null(typeof(CertificateProjection).GetProperty("CefrLevel"));
        Assert.Null(typeof(CertificateProjection).GetProperty("AwardedLevel"));
    }

    // -----------------------------------------------------------------------------------------
    // AC2: a certificate that cannot express a level records only its display ceiling.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a C2 coordinate on a TOEIC skill. The
    /// C2 row is a <c>display_ceiling</c> row: it exists so 495 can be printed, and it is the trap
    /// that a projector reading "the row for this level" would return with
    /// <c>AwardedLevel = C2</c>. The assertions that catch it are <c>null</c> level +
    /// <c>DisplayCeiling</c> outcome + the score equal to the scale top, which no real C2 claim can
    /// satisfy at once.
    /// </summary>
    [Fact]
    public async Task A_level_the_certificate_cannot_express_emits_the_ceiling_and_never_a_level()
    {
        var certCode = Unique("PROJC");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST", "READ"],
            [
                (0, "C1", CutScoreRowKind.Cut, 490, 495),
                (0, "C2", CutScoreRowKind.DisplayCeiling, null, 495),
                (1, "C1", CutScoreRowKind.Cut, 455, 495),
                (1, "C2", CutScoreRowKind.DisplayCeiling, null, 495),
            ]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.C2, 100), (1, CefrLevel.C2, 0)));

        foreach (var skill in projection.Skills)
        {
            Assert.Null(skill.AwardedLevel);
            Assert.Equal(ProjectionOutcome.DisplayCeiling, skill.Outcome);
            Assert.Equal(495, skill.ProjectedScore);
            Assert.Equal(495, skill.DisplayCeiling);
        }

        // The ceiling is a number on the certificate's scale, so a certificate whose two skills are
        // both 495 can still report its own total. It must not become "C2".
        Assert.Equal(990, projection.CombinedTotal);
        Assert.Equal(ProjectionOutcome.DisplayCeiling, projection.Outcome);
    }

    // -----------------------------------------------------------------------------------------
    // The nearest-row trap.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> B2 with no B2 row, when B1 and C1 rows
    /// exist. A lookup written as "the nearest row at or below the level" answers with B1's range and
    /// looks entirely reasonable; so does "the nearest row at or above". Both are inventions, so the
    /// only defensible answer is that the table does not answer this, which is <c>cannot_judge</c>.
    /// </summary>
    [Fact]
    public async Task A_missing_level_row_is_cannot_judge_and_never_the_nearest_row()
    {
        var certCode = Unique("PROJN");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST"],
            [
                (0, "B1", CutScoreRowKind.Cut, 275, 395),
                (0, "C1", CutScoreRowKind.Cut, 490, 495),
            ]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B2, 50)));

        var skill = projection.Skills.Single();
        Assert.Equal(ProjectionOutcome.CannotJudge, skill.Outcome);
        Assert.Equal(CefrProjection.NoCutScoreRowForLevel, skill.CannotJudgeReason);
        Assert.Null(skill.ProjectedScore);
        Assert.Null(skill.AwardedLevel);

        // A total built from a skill that was not projected is the prorated number this ticket
        // refuses; the combined total stays absent rather than being derived from the other skills.
        Assert.Null(projection.CombinedTotal);
        Assert.Equal(ProjectionOutcome.CannotJudge, projection.Outcome);
    }

    // -----------------------------------------------------------------------------------------
    // AC5: no study-hour evidence resolves to cannot-judge, not to a prorated number.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// LVL-16: the certificates with no CEFR-named band (CAT_B1/CAT_B2, SW_B1/SW_B2) carry NULL
    /// study hours, and populating an equivalent coordinate "requires an owner decision, not a
    /// lookup".
    /// <para>
    /// <b>The input that passes a wrong implementation:</b> a certificate with perfectly good cut
    /// rows but NULL study hours on every band. A projector that only checks for cut rows happily
    /// returns 335, which is a prorated-looking number with no evidence behind it. The assertion that
    /// catches it is the <i>reason</i>, not just the null score: another guard could also return null.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bands_without_study_hour_evidence_resolve_to_cannot_judge()
    {
        var certCode = Unique("PROJH");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: false, ["LIST"],
            [(0, "B1", CutScoreRowKind.Cut, 275, 395)]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 50)));

        var skill = projection.Skills.Single();
        Assert.Equal(ProjectionOutcome.CannotJudge, skill.Outcome);
        Assert.Equal(CefrProjection.BandsCarryNoStudyHours, skill.CannotJudgeReason);
        Assert.Null(skill.ProjectedScore);
        Assert.Null(projection.CombinedTotal);
    }

    /// <summary>
    /// A certificate with no cut-score version at all is cannot-judge for a different reason, and the
    /// two reasons must not be the same string: a screen that says "no study-hour evidence" for a
    /// certificate that simply has no table sends the admin to the wrong page.
    /// </summary>
    [Fact]
    public async Task A_certificate_with_no_cut_score_table_is_cannot_judge_for_that_reason()
    {
        var certCode = Unique("PROJ0");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST"], [],
            publishesVersion: false);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 50)));

        Assert.Equal(ProjectionOutcome.CannotJudge, projection.Outcome);
        Assert.Equal(CefrProjection.NoCutScoreTable, projection.CannotJudgeReason);
        Assert.Null(projection.CombinedTotal);
        Assert.Null(projection.CutScoreTableVersion);
    }

    // -----------------------------------------------------------------------------------------
    // AC3: the three aggregation rules.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_sum_rule_adds_the_two_section_scores()
    {
        var certCode = Unique("PROJS");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST", "READ"],
            [
                (0, "B1", CutScoreRowKind.Cut, 275, 395),
                (1, "B2", CutScoreRowKind.Cut, 385, 450),
            ]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 50), (1, CefrLevel.B2, 40)));

        Assert.Equal(746, projection.CombinedTotal);
        Assert.Equal("sum", projection.AggregationRule);
    }

    /// <summary>
    /// IELTS's rule, in the tenths its scale is stored in: 4.0 and 5.5 average to 4.75, which rounds
    /// to the nearest half band, 5.0 (stored 50). A test that asserted the plain average (47) would
    /// pass an implementation that forgot the rounding the certificate's rule specifies.
    /// </summary>
    [Fact]
    public async Task The_average_rule_averages_and_rounds_to_the_half_step()
    {
        var certCode = Unique("PROJA");
        var (certId, skills) = await SeedAsync(
            certCode, "avg_half_step", bandsCarryStudyHours: true, ["LIST", "READ"],
            [
                (0, "B1", CutScoreRowKind.Cut, 40, 50),
                (1, "B2", CutScoreRowKind.Cut, 55, 65),
            ]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 0), (1, CefrLevel.B2, 0)));

        Assert.Equal(40, projection.Skills[0].ProjectedScore);
        Assert.Equal(55, projection.Skills[1].ProjectedScore);
        Assert.Equal(50, projection.CombinedTotal);
    }

    /// <summary>
    /// <b>The input that passes a wrong implementation:</b> a two-skill certificate whose skills
    /// project perfectly well. Any implementation that always computes a total returns something
    /// here; TOEIC S&amp;W reports no combined total at all, so the assertion is that the total is
    /// absent while both skill scores are present.
    /// </summary>
    [Fact]
    public async Task A_separate_skills_certificate_reports_no_combined_total()
    {
        var certCode = Unique("PROJX");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["SPEA", "WRIT"],
            [
                (0, "B1", CutScoreRowKind.Cut, 120, 150),
                (1, "B2", CutScoreRowKind.Cut, 150, 170),
            ],
            reportsSkillsSeparately: true);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.B1, 50), (1, CefrLevel.B2, 50)));

        Assert.Null(projection.CombinedTotal);
        Assert.True(projection.ReportsSkillsSeparately);
        Assert.Equal(135, projection.Skills[0].ProjectedScore);
        Assert.Equal(160, projection.Skills[1].ProjectedScore);
        Assert.Equal(ProjectionOutcome.Projected, projection.Outcome);
    }

    /// <summary>
    /// The <c>uncertified</c> row kind: IELTS and VSTEP score below A1 without certifying it. The
    /// value is real and projectable; the level is never awarded, so a Pre-A1 coordinate cannot come
    /// back as "below A1" beside a band the certificate does not issue.
    /// </summary>
    [Fact]
    public async Task An_uncertified_range_projects_a_score_but_awards_no_level()
    {
        var certCode = Unique("PROJU");
        var (certId, skills) = await SeedAsync(
            certCode, "avg_half_step", bandsCarryStudyHours: true, ["LIST"],
            [(0, "pre_a1", CutScoreRowKind.Uncertified, 0, 15)]);

        var projection = await CreateService().ProjectAsync(
            certId, Coordinates(skills, (0, CefrLevel.PreA1, 100)));

        var skill = projection.Skills.Single();
        Assert.Equal(ProjectionOutcome.Uncertified, skill.Outcome);
        Assert.Equal(15, skill.ProjectedScore);
        Assert.Null(skill.AwardedLevel);
    }

    // -----------------------------------------------------------------------------------------
    // AC1: versioned, so a recorded projection can be reproduced.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The version is load-bearing: a re-standardised table must not silently change a result that
    /// was already recorded. <b>The input that passes a wrong implementation:</b> two versions with
    /// different B1 ranges, asking for the old one. An implementation that ignores the requested
    /// version and takes the latest returns 350 for both calls and every assertion below holds
    /// except the two score comparisons.
    /// </summary>
    [Fact]
    public async Task A_recorded_projection_can_be_reproduced_from_the_version_it_names()
    {
        var certCode = Unique("PROJV");
        var (certId, skills) = await SeedAsync(
            certCode, "sum", bandsCarryStudyHours: true, ["LIST"],
            [
                (0, "B1", CutScoreRowKind.Cut, 275, 395),
                (0, "B2", CutScoreRowKind.Cut, 400, 485),
            ],
            tableVersion: 1, source: "study one");

        await database.SqlAsync($"""
            DECLARE @c INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
              VALUES (@c, 2, 0, N'study two', '2025-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@c, 2, {skills[0]}, 'B1', 'cut', 300, 480),
              (@c, 2, {skills[0]}, 'B2', 'cut', 500, 590);
            """);

        var service = CreateService();
        var coordinates = Coordinates(skills, (0, CefrLevel.B1, 50));

        var asRecorded = await service.ProjectAsync(certId, coordinates, tableVersion: 1);
        var latest = await service.ProjectAsync(certId, coordinates);

        Assert.Equal(1, asRecorded.CutScoreTableVersion);
        Assert.Equal(335, asRecorded.Skills.Single().ProjectedScore);
        Assert.Equal(1, asRecorded.Skills.Single().CutScoreTableVersion);

        // The default is the newest version, and it is a different answer - which is exactly why the
        // version has to travel with a recorded projection.
        Assert.Equal(2, latest.CutScoreTableVersion);
        Assert.Equal(390, latest.Skills.Single().ProjectedScore);
        Assert.NotEqual(asRecorded.Skills.Single().ProjectedScore, latest.Skills.Single().ProjectedScore);
    }

    // -----------------------------------------------------------------------------------------
    // Drift: the row-kind vocabulary, and PRN-03 held structurally.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// ADR-0006: a code vocabulary that reaches the database carries a drift test against its CHECK.
    /// </summary>
    [Fact]
    public void The_row_kind_enum_matches_its_check_constraint()
    {
        var schema = File.ReadAllText(Path.Combine(DatabaseFixture.RepoRootPath, "docs", "database-schema.sql"));
        var match = System.Text.RegularExpressions.Regex.Match(
            schema, @"CONSTRAINT CK_CefrCutScores_RowKind CHECK \(RowKind IN \((?<codes>[^)]*)\)\)");

        Assert.True(match.Success, "CK_CefrCutScores_RowKind not found in docs/database-schema.sql");

        var schemaCodes = System.Text.RegularExpressions.Regex
            .Matches(match.Groups["codes"].Value, "'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .Order(StringComparer.Ordinal);

        var enumCodes = Enum.GetValues<CutScoreRowKind>().Select(kind => kind.ToCode()).Order(StringComparer.Ordinal);

        Assert.Equal(schemaCodes, enumCodes);
    }

    /// <summary>
    /// The cut-score level column accepts everything a result may report, the Pre-A1 floor included,
    /// because §4.6 has a Pre-A1 row. This is deliberately <i>not</i> the authored ladder: the floor
    /// is reportable and never authored, which is the distinction #182 put in the type.
    /// </summary>
    [Fact]
    public void The_cut_score_level_column_accepts_exactly_the_reportable_levels()
    {
        var schema = File.ReadAllText(Path.Combine(DatabaseFixture.RepoRootPath, "docs", "database-schema.sql"));
        var match = System.Text.RegularExpressions.Regex.Match(
            schema, @"CONSTRAINT CK_CefrCutScores_Level CHECK \(CefrLevel IN \((?<codes>[^)]*)\)\)");

        Assert.True(match.Success, "CK_CefrCutScores_Level not found in docs/database-schema.sql");

        var schemaCodes = System.Text.RegularExpressions.Regex
            .Matches(match.Groups["codes"].Value, "'([A-Za-z0-9_]+)'")
            .Select(m => m.Groups[1].Value)
            .Order(StringComparer.Ordinal);

        Assert.Equal(CefrLevel.Reportable.Select(level => level.Code).Order(StringComparer.Ordinal), schemaCodes);
    }

    /// <summary>
    /// PRN-03, held structurally for this ticket: the per-skill projection may carry a level, and the
    /// certificate-level result may carry a score. No member of the certificate-level result is a
    /// level, so a caller cannot label a total with one even by accident.
    /// </summary>
    [Fact]
    public void The_certificate_level_result_has_no_level_member()
    {
        var members = typeof(CertificateProjection)
            .GetProperties()
            .Select(p => p.PropertyType)
            .ToList();

        Assert.DoesNotContain(typeof(CefrLevel), members);
        Assert.DoesNotContain(typeof(CefrLevel?), members);
        Assert.Contains(typeof(int?), members);   // the combined score is a score
    }

    /// <summary>The EF model must declare the trigger, or every save against the table fails on OUTPUT.</summary>
    [Fact]
    public void The_overlap_trigger_is_declared_on_the_entity()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var entity = db.Model.FindEntityType(typeof(CefrCutScore));

        Assert.NotNull(entity);
        Assert.Contains("trg_CefrCutScores_NoOverlap", entity.GetDeclaredTriggers().Select(t => t.ModelName));
    }

    /// <summary>
    /// §4.6's numbers reach the seeded certificates, and the three row kinds survive the trip. A seed
    /// that stored every row as a plain <c>cut</c> would give TOEIC a real C2 level — the exact claim
    /// LVL-14 refuses — while looking like a complete table.
    /// </summary>
    [Fact]
    public async Task The_seeded_cut_scores_carry_three_row_kinds_and_no_C2_level_for_TOEIC()
    {
        // #269: on its OWN database. This used to run the whole seed against the shared fixture
        // database, twice if two runs overlapped, and a second load inserting ScoreBands beside the
        // first trips trg_ScoreBands_NoOverlap - "ranges may not overlap within a certificate". The
        // test then failed for a reason that had nothing to do with the fact it names. DemoSeedTests
        // already uses a per-test database for exactly this reason.
        var dbName = $"EE_CefrSeed_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                dbName, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

        var toeicId = await db.Certificates.Where(c => c.Code == "TOEIC").Select(c => c.CertId).SingleAsync();

        var toeicC2 = await db.CefrCutScores
            .Where(r => r.CertId == toeicId && r.CefrLevel == "C2")
            .ToListAsync();

        Assert.Equal(2, toeicC2.Count);
        Assert.All(toeicC2, row =>
        {
            Assert.Equal(CutScoreRowKind.DisplayCeiling.ToCode(), row.RowKind);
            Assert.Null(row.MinScaled);      // no range, so no level can be read from it
            Assert.Equal(495, row.MaxScaled); // ... only the top of the scale
        });

        // VSTEP has no C2 band either, and its below-B1 rows are uncertified rather than cuts.
        var vstepId = await db.Certificates.Where(c => c.Code == "VSTEP").Select(c => c.CertId).SingleAsync();
        var vstep = await db.CefrCutScores.Where(r => r.CertId == vstepId).ToListAsync();
        Assert.Contains(vstep, r => r.CefrLevel == "C2" && r.RowKind == CutScoreRowKind.DisplayCeiling.ToCode());
        Assert.Contains(vstep, r => r.CefrLevel == "A2" && r.RowKind == CutScoreRowKind.Uncertified.ToCode());

        // IELTS alone certifies C2, so its C2 rows are real cuts with a range.
        var ieltsId = await db.Certificates.Where(c => c.Code == "IELTS").Select(c => c.CertId).SingleAsync();
        var ieltsC2 = await db.CefrCutScores
            .Where(r => r.CertId == ieltsId && r.CefrLevel == "C2")
            .ToListAsync();
        Assert.Equal(4, ieltsC2.Count);
        Assert.All(ieltsC2, row =>
        {
            Assert.Equal(CutScoreRowKind.Cut.ToCode(), row.RowKind);
            Assert.NotNull(row.MinScaled);
        });

        // LVL-12's worked example: 785 is the sum of Listening 400 + Reading 385, and the two cuts
        // are independent, so the same total can hold two different levels.
        var toeicVersion = await db.CefrCutScoreVersions
            .SingleAsync(v => v.CertId == toeicId && v.TableVersion == 1);
        Assert.False(toeicVersion.ReportsSkillsSeparately);
        Assert.Contains("ETS", toeicVersion.Source);

        var listingSkillId = await db.Skills
            .Where(s => s.CertId == toeicId && s.Code == "LIST").Select(s => s.SkillId).SingleAsync();
        var readingSkillId = await db.Skills
            .Where(s => s.CertId == toeicId && s.Code == "READ").Select(s => s.SkillId).SingleAsync();

        var listeningB2 = await db.CefrCutScores.SingleAsync(r =>
            r.CertId == toeicId && r.SkillId == listingSkillId && r.CefrLevel == "B2");
        var readingB2 = await db.CefrCutScores.SingleAsync(r =>
            r.CertId == toeicId && r.SkillId == readingSkillId && r.CefrLevel == "B2");

        Assert.Equal(400, listeningB2.MinScaled);
        Assert.Equal(385, readingB2.MinScaled);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
