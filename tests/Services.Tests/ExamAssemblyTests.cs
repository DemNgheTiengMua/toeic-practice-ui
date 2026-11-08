using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #225 / S-09: the assembly workbench's read model — what a section declares against how much
/// <b>servable</b> stock the pool can supply, plus the positions the paper already holds.
/// <para>
/// Every test runs on its own database, because each one asserts an exact supply count and the shared
/// fixture already holds stock written by sixty other classes. <c>database.CreateFreshDatabaseAsync</c>
/// loads the real schema — triggers and all — so a count that is wrong because of a stale figure fails
/// here rather than on a screen.
/// </para>
/// <para>
/// <b>The trap this file is built around.</b> A supply count that ignores <c>LifecycleStatus</c>
/// promises stock a learner is never served, and one that reads a certificate off an item reads a
/// column that does not exist since #184. Each test below seeds the wrong kind of item <em>beside</em>
/// the right kind, so an implementation that counted rows rather than servable rows reports a
/// different number than the assertion and fails.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExamAssemblyTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private static AdminExamService CreateService(CompanionDbContext db) => new(db, TimeProvider.System);

    private async Task InFreshDatabaseAsync(string suffix, Func<string, Task> body)
    {
        var name = $"EEAssemb{suffix}_{Guid.NewGuid():N}"[..30];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await body(connectionString);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A certificate with one section per requested skill, an exam of the requested tier, and the
    /// canonical topics the items need. Returns the ids the tests act on.
    /// </summary>
    private static async Task<Fixture> SeedPaperAsync(
        string connectionString,
        (string Code, string Modality, int Declared)[] sections,
        string examTier = "demo")
    {
        await using var db = DbContextFactory.Create(connectionString);

        foreach (var topic in new[]
                 {
                     ("listening", "L-DET"), ("reading", "R-DET"), ("reading", "R-GRAM"),
                 })
        {
            // A second paper in the same catalog shares the canonical topics, so each row is guarded
            // individually rather than behind one table-level check.
            if (!await db.Topics.AnyAsync(t => t.Modality == topic.Item1 && t.Code == topic.Item2))
            {
                db.Topics.Add(new Topic { Modality = topic.Item1, Code = topic.Item2, Name = topic.Item2 });
            }
        }

        await db.SaveChangesAsync();

        var cert = new Certificate { Code = Unique("C"), Name = "Assembly cert", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var exams = new Exam
        {
            CertId = cert.CertId,
            Name = "Assembly paper",
            Status = "draft",
            DurationMinutes = 60,
            Purpose = "mock",
            ContentTier = examTier,
        };
        db.Exams.Add(exams);

        var sectionIds = new List<int>();
        for (var index = 0; index < sections.Length; index++)
        {
            var definition = sections[index];
            var skill = new Skill
            {
                CertId = cert.CertId,
                Code = $"S{index}",
                Name = definition.Modality,
                Modality = definition.Modality,
                DisplayOrder = index + 1,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                SkillId = skill.SkillId,
                CertId = cert.CertId,
                Modality = definition.Modality,
                Code = definition.Code,
                Name = definition.Code,
                OptionCount = 4,
                ExpectedQuestionCount = definition.Declared,
                DisplayOrder = index + 1,
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();
            sectionIds.Add(section.SectionId);
        }

        await db.SaveChangesAsync();

        return new Fixture(
            cert.CertId,
            exams.ExamId,
            sectionIds,
            (await db.Topics.SingleAsync(t => t.Modality == "reading" && t.Code == "R-DET")).TopicId,
            (await db.Topics.SingleAsync(t => t.Modality == "reading" && t.Code == "R-GRAM")).TopicId,
            (await db.Topics.SingleAsync(t => t.Modality == "listening" && t.Code == "L-DET")).TopicId);
    }

    private sealed record Fixture(
        int CertId, int ExamId, IReadOnlyList<int> SectionIds, int ReadingTopic, int SecondReadingTopic, int ListeningTopic);

    /// <summary>One pool item plus its version 1. The caller decides every fact the count reads.</summary>
    private static async Task<int> SeedItemAsync(
        string connectionString, int topicId, string modality, string lifecycle, string tier, string level = "B1")
    {
        await using var db = DbContextFactory.Create(connectionString);

        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = CefrLevel.TryParseAuthored(level, out var parsed) ? parsed : CefrLevel.B1,
            ItemType = modality == "reading" ? "sentence_cloze" : "short_exchange",
            ContentTier = tier,
            LifecycleStatus = lifecycle,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = $"Stem {item.ItemId}",
            OptionA = "a",
            OptionB = "b",
            OptionC = "c",
            OptionD = "d",
            KeyLabel = "A",
            Explanation = "because",
        });
        await db.SaveChangesAsync();

        return item.ItemId;
    }

    /// <summary>
    /// AC1's first half, and the trap the ticket names first: <b>only <c>active</c> stock is supply</b>.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> an implementation counting every item —
    /// or every item that is not <em>retired</c> — reports 7 here. The fixture holds one row in each
    /// of the six lifecycle states plus a second active row, so "count the table" and "count what a
    /// learner is served" are different numbers (7 vs 2), and the declared count is 5 so the shortfall
    /// is real either way.
    /// </para>
    /// </summary>
    [Fact]
    public Task Supply_counts_only_active_versions() =>
        InFreshDatabaseAsync("Active", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 5)]);

            foreach (var lifecycle in new[] { "draft", "validated", "quarantined", "retired", "retired_defective" })
            {
                await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", lifecycle, "demo");
            }

            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, fixture.SecondReadingTopic, "reading", "active", "demo");

            await using var db = DbContextFactory.Create(connectionString);
            var assembly = await CreateService(db).GetExamAssemblyAsync(fixture.ExamId);

            var section = Assert.Single(assembly.Sections);
            Assert.Equal(5, section.DeclaredCount);
            Assert.Equal(2, section.SupplyCount);
            Assert.Equal(3, section.Shortfall);
            Assert.Equal(2, section.Candidates.Count);
        });

    /// <summary>
    /// AC1's second half: <b>the cell is the section's skill, not a certificate</b>. Items carry no
    /// <c>CertId</c> since #184, so a count that tried to read one would not compile; what can go wrong
    /// is the skill axis.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a count that ignored the modality reports
    /// 4 on the reading section (2 reading + 2 listening). The two skills are seeded together and the
    /// assertion is on both sections at once, so a count that mixed them fails on both.
    /// </para>
    /// </summary>
    [Fact]
    public Task Supply_is_counted_per_section_skill_not_across_the_pool() =>
        InFreshDatabaseAsync("Skill", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P1", "listening", 3), ("P5", "reading", 3)]);

            await SeedItemAsync(connectionString, fixture.ListeningTopic, "listening", "active", "demo");
            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, fixture.SecondReadingTopic, "reading", "active", "demo");

            // Demo stock a reading section must not count at all: same skill, wrong lifecycle.
            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "retired", "demo");

            await using var db = DbContextFactory.Create(connectionString);
            var assembly = await CreateService(db).GetExamAssemblyAsync(fixture.ExamId);

            var listening = Assert.Single(assembly.Sections, s => s.Modality == ModalityDto.Listening);
            var reading = Assert.Single(assembly.Sections, s => s.Modality == ModalityDto.Reading);

            Assert.Equal(1, listening.SupplyCount);
            Assert.Equal(2, listening.Shortfall);
            Assert.Equal(2, reading.SupplyCount);
            Assert.Equal(1, reading.Shortfall);
        });

    /// <summary>
    /// AC3's supply half: <b>a production paper may only draw production stock</b> (INV-6), while a
    /// demo paper may draw either tier — the trigger refuses one direction only, and a count that
    /// filtered both ways would understate what a demo paper can fill.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> an implementation that ignores the tier
    /// entirely reports 3 for the production paper; one that filtered a demo paper to demo stock alone
    /// reports 1 for it. Both numbers are asserted.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_production_paper_counts_only_production_stock_and_a_demo_paper_counts_both() =>
        InFreshDatabaseAsync("Tier", async connectionString =>
        {
            var production = await SeedPaperAsync(connectionString, [("P5", "reading", 2)], examTier: "production");
            var demo = await SeedPaperAsync(connectionString, [("P6", "reading", 2)], examTier: "demo");

            await SeedItemAsync(connectionString, production.ReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, production.SecondReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, production.ReadingTopic, "reading", "active", "production");

            await using var db = DbContextFactory.Create(connectionString);
            var service = CreateService(db);

            var productionPaper = await service.GetExamAssemblyAsync(production.ExamId);
            var demoPaper = await service.GetExamAssemblyAsync(demo.ExamId);

            Assert.Equal(1, Assert.Single(productionPaper.Sections).SupplyCount);
            Assert.Equal(1, Assert.Single(productionPaper.Sections).Shortfall);

            // The demo paper sees all three: two demo rows and the promoted one.
            Assert.Equal(3, Assert.Single(demoPaper.Sections).SupplyCount);
            Assert.Equal(0, Assert.Single(demoPaper.Sections).Shortfall);
        });

    /// <summary>
    /// AC1's screen half: <b>declared and supply are different numbers in the fixture, and the
    /// shortfall is their gap.</b> A test where the two agree cannot tell a screen that prints one
    /// number twice from one that prints both.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a shortfall computed as
    /// <c>supply - declared</c> reads <c>-2</c> here; one floored to zero regardless reports 0. The
    /// assertion is on the exact positive gap, and a second section that is over-supplied proves the
    /// figure floors at zero rather than going negative.
    /// </para>
    /// </summary>
    [Fact]
    public Task The_shortfall_is_the_declared_count_less_supply_and_floors_at_zero() =>
        InFreshDatabaseAsync("Gap", async connectionString =>
        {
            var fixture = await SeedPaperAsync(
                connectionString, [("P5", "reading", 5), ("P6", "reading", 1)]);

            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, fixture.SecondReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");

            await using var db = DbContextFactory.Create(connectionString);
            var assembly = await CreateService(db).GetExamAssemblyAsync(fixture.ExamId);

            // Both sections are reading, so both see the same three active versions of that skill: the
            // pool's depth for the cell, not an allocation between sections.
            var declaredFive = Assert.Single(assembly.Sections, s => s.Code == "P5");
            var declaredOne = Assert.Single(assembly.Sections, s => s.Code == "P6");

            Assert.Equal(5, declaredFive.DeclaredCount);
            Assert.Equal(3, declaredFive.SupplyCount);
            Assert.Equal(2, declaredFive.Shortfall);

            Assert.Equal(1, declaredOne.DeclaredCount);
            Assert.Equal(3, declaredOne.SupplyCount);
            Assert.Equal(0, declaredOne.Shortfall);
        });

    /// <summary>
    /// AC2 through the workbench's own read: <b>binding records the position and the unit the paper
    /// needs</b>, and the workbench then shows that position filled while the declared shape keeps the
    /// slots the paper has not reached.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a read that renders only bound positions
    /// shows one row for a section that declares five, so the declared shape would be invisible. The
    /// assertion is on the count of positions as well as the bound one's unit.
    /// </para>
    /// </summary>
    [Fact]
    public Task A_bound_position_carries_its_unit_and_the_declared_shape_stays_visible() =>
        InFreshDatabaseAsync("Pos", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 5)]);
            var itemId = await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var versionId = await db.ItemVersions.Where(v => v.ItemId == itemId)
                    .Select(v => v.ItemVersionId).SingleAsync();

                await CreateService(db).BindItemAsync(
                    fixture.ExamId,
                    new BindPoolItemRequest(fixture.SectionIds[0], versionId, DisplayOrder: 3, UnitNumber: 2));
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var section = Assert.Single((await CreateService(db).GetExamAssemblyAsync(fixture.ExamId)).Sections);

                Assert.Equal(1, section.BoundCount);
                Assert.Equal(5, section.Positions.Count);

                var bound = Assert.Single(section.Positions, p => p.IsDeclared && p.ItemVersionId is not null);
                Assert.Equal(3, bound.DisplayOrder);
                Assert.Equal(2, bound.UnitNumber);
                Assert.Equal("R-DET", bound.TopicCode);

                // The other four declared positions are open, and they are still rows.
                Assert.Equal(4, section.Positions.Count(p => p.IsDeclared && p.ItemVersionId is null));
            }
        });

    /// <summary>
    /// The supply total must not drift from the list it is shown beside: a badge saying "6 servable"
    /// above five rows is the defect that makes an author hunt for a version the screen will not show.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a count taken before the newest-version
    /// join counts an item whose versions were all deleted; a count taken after it does not. Here the
    /// two agree by construction, and the assertion is that they always do: supply equals the
    /// candidate total, which is what the page prints.
    /// </para>
    /// </summary>
    [Fact]
    public Task Supply_equals_the_candidate_total_the_screen_lists() =>
        InFreshDatabaseAsync("Cand", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 2)]);

            for (var i = 0; i < 3; i++)
            {
                await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");
            }

            await using var db = DbContextFactory.Create(connectionString);
            var section = Assert.Single((await CreateService(db).GetExamAssemblyAsync(fixture.ExamId)).Sections);

            Assert.Equal(section.SupplyCount, section.CandidateTotal);
            Assert.Equal(3, section.Candidates.Count);
            // Newest version of each item, and nothing else: one version per item.
            Assert.All(section.Candidates, c => Assert.Equal(1, c.VersionNumber));
        });

    /// <summary>
    /// AC3's other half at the service seam: <b>a version from the wrong skill is refused and no
    /// binding row is written.</b>
    /// <para>
    /// The schema refuses the pair through <c>FK_ExamItems_ItemSkill</c>; <c>BindItemAsync</c> refuses
    /// it earlier with a sentence naming both skills. <c>domaintest23</c> proves the constraint in
    /// T-SQL — this proves the caller sees a refusal rather than a 500, and that nothing was written.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a fixture where every item is reading
    /// cannot express "wrong skill" at all — the bind would succeed and this test would fail, which is
    /// the point. Two skills are seeded and the bind crosses them.
    /// </para>
    /// </summary>
    [Fact]
    public Task Binding_a_version_from_the_wrong_skill_is_refused_and_writes_nothing() =>
        InFreshDatabaseAsync("WrongSkill", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 2)]);

            var listeningItem = await SeedItemAsync(
                connectionString, fixture.ListeningTopic, "listening", "active", "demo");

            await using var db = DbContextFactory.Create(connectionString);
            var listeningVersion = await db.ItemVersions.Where(v => v.ItemId == listeningItem)
                .Select(v => v.ItemVersionId).SingleAsync();

            var refused = await Assert.ThrowsAsync<AdminExamException>(() =>
                CreateService(db).BindItemAsync(
                    fixture.ExamId,
                    new BindPoolItemRequest(fixture.SectionIds[0], listeningVersion, DisplayOrder: 1)));

            Assert.Contains("own skill", refused.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await db.ExamItems.CountAsync(b => b.ExamId == fixture.ExamId));
        });

    /// <summary>
    /// AC3's first half, asserted the way the ticket demands it — <b>on the row count, not the
    /// message.</b> The refusal itself is the trigger's (<c>trg_ExamItems_ProductionBindsProduction</c>,
    /// error 50049) and is already proved by the schema test and by
    /// <c>AdminItemAuthoringTests</c>; what this adds is that the workbench's own read never claims the
    /// bind happened: supply on the production paper counts production stock only, so the demo item
    /// the bind refused was never numbered as fillable.
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> an implementation that pre-checked the
    /// tier in the service and returned a cheerful DTO would leave zero rows — so this test alone
    /// cannot prove the trigger fired. That is why the schema test owns the trigger proof and this one
    /// owns "the screen's supply figure did not include the demo row".
    /// </para>
    /// </summary>
    [Fact]
    public Task A_production_paper_never_offers_a_demo_version_it_could_not_bind() =>
        InFreshDatabaseAsync("Inv6", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 2)], examTier: "production");
            await SeedItemAsync(connectionString, fixture.ReadingTopic, "reading", "active", "demo");
            await SeedItemAsync(connectionString, fixture.SecondReadingTopic, "reading", "active", "demo");

            await using var db = DbContextFactory.Create(connectionString);
            var section = Assert.Single((await CreateService(db).GetExamAssemblyAsync(fixture.ExamId)).Sections);

            Assert.Equal(0, section.SupplyCount);
            Assert.Equal(2, section.Shortfall);
            Assert.Empty(section.Candidates);
            Assert.Equal(0, await db.ExamItems.CountAsync(b => b.ExamId == fixture.ExamId));
        });

    /// <summary>
    /// #157's lock, surfaced as data rather than as a refusal: a paper a learner already finished
    /// reports its finished-attempt count, which is what the screen turns into "no bind controls".
    /// The read still works — an audit has to see the paper those attempts were scored against.
    /// </summary>
    [Fact]
    public Task A_paper_with_a_finished_attempt_still_reads_and_reports_the_count() =>
        InFreshDatabaseAsync("Lock", async connectionString =>
        {
            var fixture = await SeedPaperAsync(connectionString, [("P5", "reading", 1)]);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                db.Roles.Add(new Role { Code = "learner", Name = "Learner" });
                await db.SaveChangesAsync();

                // trg_ExamAttempts_PublishedOnly refuses an attempt on a draft paper, so the lock
                // fixture publishes first — a shape the screen never invents.
                var exam = await db.Exams.SingleAsync(e => e.ExamId == fixture.ExamId);
                exam.Status = "published";
                await db.SaveChangesAsync();

                var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).SingleAsync();

                var user = new User
                {
                    Email = $"{Unique("u")}@test.local",
                    FullName = "Attempt owner",
                    PasswordHash = [1],
                    RoleId = roleId,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                db.ExamAttempts.Add(new ExamAttempt
                {
                    UserId = user.UserId,
                    ExamId = fixture.ExamId,
                    CertId = fixture.CertId,
                    Status = "submitted",
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    ExpiresAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var assembly = await CreateService(db).GetExamAssemblyAsync(fixture.ExamId);
                Assert.Equal(1, assembly.FinishedAttemptCount);
                Assert.Single(assembly.Sections);
            }
        });
}
