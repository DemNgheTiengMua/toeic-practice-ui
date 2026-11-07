using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #208: the rubric and grader calibration studio's service, against the real schema.
/// <para>
/// The point of every test here is the hole the ticket closes: a rubric with all 28 cells could be locked
/// while its ladder had never run, because the lock trigger checked the grid's SHAPE and nothing checked
/// its SEPARATION. Each guard below states, in its own comment, the input that would pass it while being
/// wrong.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RubricStudioTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE208_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public RubricStudioTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A certificate with one writing skill, the canonical four criteria and a complete 28-cell grid.
    /// Seeded into THIS class's database, never the run-wide fixture catalog: a certificate seeded there
    /// leaks into every other test class, which has broken the full suite three times in this rebuild.
    /// </summary>
    private async Task<(int CertId, int SkillId)> SeedAsync(string certCode, bool completeGrid = true)
    {
        var script = Path.Combine(Path.GetTempPath(), $"ee208-{Guid.NewGuid():N}.sql");
        File.WriteAllText(script, $"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'WRIT', N'Writing', 'writing', 1, 60);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder)
            VALUES (@cert, @sW, 'W_TR', N'Task fulfilment', N'Prose for W_TR.', 6.0, 1.0, 1),
                   (@cert, @sW, 'W_CC', N'Coherence and cohesion', N'Prose for W_CC.', 6.0, 1.0, 2),
                   (@cert, @sW, 'W_LR', N'Lexical resource', N'Prose for W_LR.', 6.0, 1.0, 3),
                   (@cert, @sW, 'W_GR', N'Grammatical range', N'Prose for W_GR.', 6.0, 1.0, 4);
            {(completeGrid ? """
            INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
              SELECT c.CriterionId, b.Band,
                     N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N': speech rate at least ' + CAST(60 + b.Band*20 AS NVARCHAR(4)) + N' wpm.',
                     N'Error-free T-units at least ' + CAST(10 + b.Band*12 AS NVARCHAR(3)) + N'%.'
                FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
               WHERE c.CertId = @cert AND c.SkillId = @sW;
            """ : "")}
            """ + "\nGO\n");
        await _database.SqlCmdFileToDatabaseAsync(_dbName, script);

        await using var db = Db();
        var cert = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var skill = await db.Skills.Where(s => s.CertId == cert).Select(s => s.SkillId).SingleAsync();
        return (cert, skill);
    }

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    /// <summary>The seven responses the ladder expects: six benchmarks and the verbose trap.</summary>
    private static IReadOnlyList<RubricLadderResponseDto> Responses() =>
        [
            .. Enumerable.Range(1, 6).Select(b => new RubricLadderResponseDto((byte)b, $"benchmark band {b}", false)),
            new RubricLadderResponseDto(3, "verbose trap, 280 words of repetitive simple clauses", true),
        ];

    private static RunRubricLadderRequest LadderRequest(int certId, int skillId) =>
        new(certId, skillId, Responses());

    // -------------------------------------------------------------------------------------------
    // AC1: the grid is four criteria by seven bands, and the service reads it as coordinates
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a read that returns "28 rows" from a query
    /// that grouped wrongly, or a grid of seven criteria by four bands. The assertion is per coordinate:
    /// every one of the four criteria has all seven bands, by number.
    /// </summary>
    [Fact]
    public async Task The_grid_reads_as_four_criteria_by_seven_bands_at_their_own_coordinates()
    {
        var certCode = Unique("RS01");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var studio = await new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System)
            .GetAsync(certId, skillId);

        Assert.Equal(4, studio.Criteria.Count);
        foreach (var criterion in studio.Criteria)
        {
            // Each criterion names its own bands, not "seven rows somewhere".
            Assert.Equal(RubricBands.All, criterion.Cells.Select(c => c.Band).OrderBy(b => b));
            Assert.Empty(criterion.MissingBands);
            Assert.All(criterion.Cells, cell =>
            {
                Assert.False(string.IsNullOrWhiteSpace(cell.Descriptor));
                Assert.False(string.IsNullOrWhiteSpace(cell.BinaryGates));
                // Descriptor and gates are DIFFERENT facts. A grid that wrote one text into both would
                // satisfy "no blanks" and grade one thing twice.
                Assert.NotEqual(cell.Descriptor, cell.BinaryGates);
            });
        }
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a save that wrote cells by array position.
    /// Seven cells posted in reverse order would land on bands 6..0 and the grid would be inverted while
    /// every count still read seven.
    /// </summary>
    [Fact]
    public async Task A_save_writes_each_cell_to_the_band_it_names_not_the_position_it_arrives_in()
    {
        var certCode = Unique("RS02");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var studio = await new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System)
            .GetAsync(certId, skillId);

        // Reverse every criterion's cells and rewrite each descriptor to name its band.
        var posted = studio.Criteria
            .Select(c => new RubricCriterionDto(
                c.CriterionId, c.Code, c.Name, c.MaxScore, c.Weight, c.DisplayOrder,
                c.Cells.Reverse().Select(cell => new RubricCellDto(
                    cell.Band, $"cell for band {cell.Band}", $"gate for band {cell.Band}")).ToList(),
                c.Anchors))
            .ToList();

        await using var save = Db();
        await new RubricStudioService(save, new FakeAiGrader(), TimeProvider.System)
            .SaveAsync(new SaveRubricRequest(certId, skillId, posted));

        await using var check = Db();
        var cells = await check.GradingCriterionDescriptors
            .Where(d => d.Criterion.SkillId == skillId)
            .ToListAsync();

        Assert.Equal(28, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Equal($"cell for band {cell.Band}", cell.Descriptor);
            Assert.Equal($"gate for band {cell.Band}", cell.BinaryGates);
        });
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a save that accepts a grid missing one band
    /// in the middle, because "28 holes" is a count and the missing band is a coordinate. The refusal must
    /// name the criterion AND the band, or the author cannot tell which row to write.
    /// </summary>
    [Fact]
    public async Task A_save_with_one_band_empty_is_refused_and_names_the_criterion_and_the_band()
    {
        var certCode = Unique("RS03");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var service = new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System);
        var studio = await service.GetAsync(certId, skillId);

        var holed = studio.Criteria.Select(c => c.Code == "W_GR"
            ? c with { Cells = c.Cells.Where(cell => cell.Band != 4).ToList() }
            : c).ToList();

        var error = await Assert.ThrowsAsync<AdminItemException>(() =>
            service.SaveAsync(new SaveRubricRequest(certId, skillId, holed)));

        Assert.Contains("W_GR", error.Message, StringComparison.Ordinal);
        Assert.Contains("band 4", error.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    // AC2: anchors at bands 2, 3, 4 attach from this screen
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a save that writes anchors for bands 2 and 4
    /// and quietly drops 3, because "there are anchors" and "more than one anchor" both pass. The three
    /// bands are asserted by name, for every criterion.
    /// </summary>
    [Fact]
    public async Task Anchors_at_two_three_and_four_attach_through_the_studio_save()
    {
        var certCode = Unique("RS04");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var service = new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System);
        var studio = await service.GetAsync(certId, skillId);
        Assert.All(studio.Criteria, c => Assert.Empty(c.Anchors));

        var withAnchors = studio.Criteria.Select(c => c with
        {
            Anchors = RubricBands.AnchorBands
                .Select(b => new RubricAnchorDto(b, $"A whole {c.Code} answer written at band {b}."))
                .ToList(),
        }).ToList();

        await service.SaveAsync(new SaveRubricRequest(certId, skillId, withAnchors));

        await using var check = Db();
        var reRead = await new RubricStudioService(check, new FakeAiGrader(), TimeProvider.System)
            .GetAsync(certId, skillId);

        foreach (var criterion in reRead.Criteria)
        {
            Assert.Equal([2, 3, 4], criterion.Anchors.Select(a => a.Band));
            Assert.Empty(criterion.MissingAnchorBands);
            Assert.All(criterion.Anchors, a =>
                Assert.Contains($"written at band {a.Band}", a.ResponseText, StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------------------------
    // AC3 + AC4: the ladder runs, plots, names the criterion, and refuses the lock
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a runner that plots one point per band and
    /// calls a flat ladder a pass. The composites rise from 1 to 6 here, so the plot is asserted in
    /// order, and the trap is asserted below its ceiling.
    /// </summary>
    [Fact]
    public async Task The_ladder_runs_seven_responses_and_plots_one_point_per_band()
    {
        var certCode = Unique("RS05");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var ladder = await new RubricStudioService(db, new OnBandGrader(), TimeProvider.System)
            .RunLadderAsync(LadderRequest(certId, skillId));

        Assert.True(ladder.Accepted, ladder.Reason);
        Assert.Equal(RubricLadderOutcomes.Accepted, ladder.Outcome);
        Assert.Equal([1, 2, 3, 4, 5, 6], ladder.Points.Where(p => !p.IsVerboseTrap).Select(p => p.Band));
        Assert.Equal(1m, ladder.Points.Where(p => !p.IsVerboseTrap).Min(p => p.Composite));
        Assert.Equal(6m, ladder.Points.Where(p => !p.IsVerboseTrap).Max(p => p.Composite));
    }

    /// <summary>
    /// AC4, the highest-value arm. <b>Which guard could this pass while being wrong:</b> a refusal that
    /// says "separation failed" and nothing else - true, and useless. The criterion that stalled must be
    /// named, and here W_GR is the only one that does not move across bands 3-4, so a runner naming all
    /// four criteria, or none, fails.
    /// </summary>
    [Fact]
    public async Task A_flat_segment_refuses_the_lock_and_names_the_criterion_responsible()
    {
        var certCode = Unique("RS06");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var ladder = await new RubricStudioService(db, new StalledCriterionGrader("W_GR"), TimeProvider.System)
            .RunLadderAsync(LadderRequest(certId, skillId));

        Assert.False(ladder.Accepted);
        // Monotonicity: band 4 scores exactly what band 3 did.
        Assert.Equal(RubricLadderOutcomes.NotMonotonic, ladder.Outcome);
        Assert.Contains("W_GR", ladder.Reason, StringComparison.Ordinal);
        Assert.Contains("W_GR", ladder.ResponsibleCriteria);
        // Not all four: naming the whole rubric sends the author to the wrong row.
        Assert.DoesNotContain("W_TR", ladder.ResponsibleCriteria);
    }

    /// <summary>
    /// AC4, the three-failure arm. <b>Which guard could this pass while being wrong:</b> any of the three
    /// collapsed into one "not calibrated". Each test below is a different rubric defect, and each must
    /// produce its own outcome code - the codes the lock trigger reads.
    /// </summary>
    [Theory]
    [InlineData("separation")]
    [InlineData("trap")]
    public async Task Each_ladder_failure_is_refused_with_its_own_outcome(string failure)
    {
        var certCode = Unique("RS07");
        var (certId, skillId) = await SeedAsync(certCode);

        // separation: every segment rises, so monotonicity says yes; one rises by 0.40, below 0.65.
        // trap: the ladder separates perfectly, and the verbose trap climbs above 3.25.
        IAiGrader grader = failure == "separation" ? new NarrowSeparationGrader() : new VerboseTrapGrader();

        await using var db = Db();
        var ladder = await new RubricStudioService(db, grader, TimeProvider.System)
            .RunLadderAsync(LadderRequest(certId, skillId));

        Assert.False(ladder.Accepted);
        Assert.Equal(
            failure == "separation" ? RubricLadderOutcomes.Separation : RubricLadderOutcomes.TrapCeiling,
            ladder.Outcome);

        if (failure == "separation")
        {
            Assert.Contains("0.65", ladder.Reason, StringComparison.Ordinal);
            // The laggard is named. W_GR moves least across the shallow step.
            Assert.Contains("W_GR", ladder.Reason, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("3.25", ladder.Reason, StringComparison.Ordinal);
            // A trap refusal is a whole-rubric failure, so no single criterion is blamed.
            Assert.Empty(ladder.ResponsibleCriteria);
        }
    }

    /// <summary>
    /// AC4, the hole itself. <b>Which guard could this pass while being wrong:</b> a lock that only
    /// checked the grid. Before #208 this test's input - a complete 28-cell rubric - locked successfully
    /// with the ladder never run. The assertion is that the lock row does not exist afterwards.
    /// </summary>
    [Fact]
    public async Task A_complete_grid_cannot_lock_without_a_passing_ladder()
    {
        var certCode = Unique("RS08");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var ladder = await new RubricStudioService(db, new StalledCriterionGrader("W_CC"), TimeProvider.System)
            .LockAsync(LadderRequest(certId, skillId));

        Assert.False(ladder.Accepted);

        await using var check = Db();
        Assert.False(await check.GradingRubricLocks.AnyAsync(l => l.SkillId == skillId));
    }

    /// <summary>
    /// The accept direction: without this, every guard above would still pass with a lock that refused
    /// every rubric. A passing ladder locks, and the verdict is stored on the row.
    /// </summary>
    [Fact]
    public async Task A_passing_ladder_locks_the_rubric_and_records_its_verdict()
    {
        var certCode = Unique("RS09");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        var ladder = await new RubricStudioService(db, new OnBandGrader(), TimeProvider.System)
            .LockAsync(LadderRequest(certId, skillId));

        Assert.True(ladder.Accepted, ladder.Reason);

        await using var check = Db();
        var lockRow = await check.GradingRubricLocks.SingleAsync(l => l.SkillId == skillId);
        Assert.Equal(RubricLadderOutcomes.Accepted, lockRow.LadderOutcome);
        Assert.NotNull(lockRow.LadderRunAt);
        Assert.False(string.IsNullOrWhiteSpace(lockRow.LadderReason));
    }

    /// <summary>
    /// The schema trigger's arm, driven from C# rather than from SQL: a lock row written by hand with a
    /// failed verdict is refused. <b>Which guard could this pass while being wrong:</b> a test that only
    /// went through <c>LockAsync</c> - the service refuses before the row is written, so the trigger
    /// would never fire and a broken trigger would ship green.
    /// </summary>
    [Fact]
    public async Task The_lock_trigger_refuses_a_row_written_directly_with_a_failed_verdict()
    {
        var certCode = Unique("RS10");
        var (certId, skillId) = await SeedAsync(certCode);

        await using var db = Db();
        db.GradingRubricLocks.Add(new BusinessObjects.Entities.GradingRubricLock
        {
            SkillId = skillId,
            CertId = certId,
            LadderOutcome = RubricLadderOutcomes.Separation,
            LadderReason = "Bands 4 and 5 are only 0.30 apart.",
            LadderRunAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("minimum separation", Flatten(error), StringComparison.Ordinal);

        await using var check = Db();
        Assert.False(await check.GradingRubricLocks.AnyAsync(l => l.SkillId == skillId));
    }

    /// <summary>
    /// AC2's second half: a locked rubric's grid is frozen, and the service says so before the trigger
    /// has to. <b>Which guard could this pass while being wrong:</b> a service that let the write reach
    /// the database - the trigger refuses it either way, so the message would be SQL Server's, which an
    /// author cannot act on.
    /// </summary>
    [Fact]
    public async Task A_locked_rubric_refuses_a_grid_save_and_says_why()
    {
        var certCode = Unique("RS11");
        var (certId, skillId) = await SeedAsync(certCode);

        await using (var lockDb = Db())
        {
            await lockDb.Database.ExecuteSqlRawAsync($"""
                INSERT INTO GradingRubricLocks (SkillId, CertId, LadderOutcome, LadderReason, LadderRunAt)
                VALUES ({skillId}, {certId}, 'accepted', N'The ladder separated every band.', SYSDATETIMEOFFSET());
                """);
        }

        await using var db = Db();
        var service = new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System);
        var studio = await service.GetAsync(certId, skillId);

        var error = await Assert.ThrowsAsync<AdminItemException>(() =>
            service.SaveAsync(new SaveRubricRequest(certId, skillId, studio.Criteria)));

        Assert.Contains("locked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A reading skill has no analytic rubric, so the studio refuses it by name.
    /// <b>Which guard could this pass while being wrong:</b> an empty grid - the page would render four
    /// criteria with no cells and an author would think the skill was merely unauthored.
    /// </summary>
    [Fact]
    public async Task A_reading_skill_is_refused_by_name_rather_than_rendered_empty()
    {
        var certCode = Unique("RS12");
        var (certId, _) = await SeedAsync(certCode);

        int readingId;
        await using (var seed = DbContextFactory.Create(_connectionString))
        {
            await seed.Database.ExecuteSqlRawAsync($"""
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES ({certId}, 'READ', N'Reading', 'reading', 3);
                """);
        }

        await using (var read = DbContextFactory.Create(_connectionString))
        {
            readingId = await read.Skills.Where(s => s.CertId == certId && s.Code == "READ").Select(s => s.SkillId).SingleAsync();
        }

        await using var db = Db();
        var error = await Assert.ThrowsAsync<AdminItemException>(() =>
            new RubricStudioService(db, new FakeAiGrader(), TimeProvider.System).GetAsync(certId, readingId));

        Assert.Contains("reading", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(Exception e)
    {
        var parts = new List<string>();
        for (var x = e; x is not null; x = x.InnerException) parts.Add(x.Message);
        return string.Join(" | ", parts);
    }

    /// <summary>Grades each criterion on its own band, so the composite equals the band.</summary>
    private sealed class OnBandGrader : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            // "benchmark band 3" is the texture of the response; band its number.
            var band = ResponseBand(request.ResponseText);
            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, band / 6m * c.MaxScore, "noted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "band-grader", scores));
        }

        internal static decimal ResponseBand(string? text)
        {
            if (text is not null && text.StartsWith("benchmark band ", StringComparison.Ordinal))
            {
                return decimal.Parse(text["benchmark band ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            }

            return 3m;   // the verbose trap, authored at band 3
        }
    }

    /// <summary>
    /// One criterion falls across bands 3-4 while the other three rise, so the COMPOSITE is flat.
    /// <para>
    /// A single stalled criterion alone cannot make the mean flat: the mean of four criteria moves by a
    /// quarter of anything one of them does, so one criterion holding still while three rise keeps the
    /// composite rising. That is why this grader makes the named criterion collapse rather than stall -
    /// the segment has to actually not rise for the monotonicity rule to be the one that fires.
    /// </para>
    /// </summary>
    private sealed class StalledCriterionGrader(string code) : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            var band = OnBandGrader.ResponseBand(request.ResponseText);
            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(
                    c.CriterionId,
                    Value(c.Code, band, code) / 6m * c.MaxScore,
                    "noted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "stalled-grader", scores));
        }

        /// <summary>Bands 1-3 climb; from band 4 the named criterion is at zero while the others climb.</summary>
        private static decimal Value(string criterionCode, decimal band, string stalled)
        {
            if (criterionCode != stalled)
            {
                return band;
            }

            return band <= 3m ? band : 0m;
        }
    }

    /// <summary>Every segment rises by 0.40, which is monotonic but below the 0.65 separation.</summary>
    private sealed class NarrowSeparationGrader : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            var band = OnBandGrader.ResponseBand(request.ResponseText);
            var composite = 1m + (band - 1m) * 0.40m;
            var scores = request.Criteria
                .Select(c =>
                {
                    // W_GR sits one step lower than the composite, so it is the laggard the runner names.
                    var value = c.Code == "W_GR" ? composite - 0.15m : composite + 0.05m;
                    return new AiCriterionScoreResult(c.CriterionId, Math.Clamp(value / 6m, 0m, 1m) * c.MaxScore, "noted");
                })
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "narrow-grader", scores));
        }
    }

    /// <summary>Perfect separation, and the trap scores 3.80 - above 3.25.</summary>
    private sealed class VerboseTrapGrader : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            var trap = request.ResponseText?.Contains("verbose trap", StringComparison.OrdinalIgnoreCase) == true;
            var value = trap ? 3.80m : OnBandGrader.ResponseBand(request.ResponseText);
            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, value / 6m * c.MaxScore, "noted"))
                .ToList();
            return Task.FromResult(new AiGradingResult(true, null, "trap-grader", scores));
        }
    }
}
