using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #215 / D7: the stage-two draw against the real schema and the real pool.
/// <para>
/// <see cref="PlacementStageTwoBlueprintTests"/> proves the routing table and the cascade as arithmetic.
/// What only the database can prove is that the candidates handed to it are the ones a stage-two draw may
/// use - active, the paper's own tier, placement-eligible for the primary tiers and not for the reserve -
/// and that the refusal happens with no <c>ExamAttempts</c> row in the table.
/// </para>
/// <para>
/// Every test runs on its own database because each asserts an exact per-band count and the shared
/// fixture already holds 60 classes' content.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementStageTwoDrawTests(DatabaseFixture database)
{
    private async Task<(string ConnectionString, string DatabaseName)> FreshAsync()
    {
        var name = $"EEStageTwo_{Guid.NewGuid():N}"[..26];
        return (await database.CreateFreshDatabaseAsync(name), name);
    }

    /// <summary>
    /// One certificate with listening and reading, plus a writing skill the objective draw must ignore -
    /// the same trap stage one's fixture uses, because "each objective skill" is the quantity under test.
    /// </summary>
    private static async Task<(int CertId, Dictionary<string, int> SkillIds)> SeedCertificateAsync(CompanionDbContext db)
    {
        db.Certificates.Add(new Certificate { Code = "S2", Name = "Stage two", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == "S2").Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2 },
            new Skill { CertId = certId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 3 });
        await db.SaveChangesAsync();

        db.Topics.AddRange(
            new Topic { Modality = "listening", Code = "L-GIST", Name = "Listening gist" },
            new Topic { Modality = "listening", Code = "L-DET", Name = "Listening detail" },
            new Topic { Modality = "reading", Code = "R-GIST", Name = "Reading gist" },
            new Topic { Modality = "writing", Code = "W-ARG", Name = "Writing argument" });
        await db.SaveChangesAsync();

        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);
        return (certId, skillIds);
    }

    private static async Task<int> SeedItemAsync(
        CompanionDbContext db,
        string modality,
        string topicCode,
        CefrLevel level,
        string lifecycle = "active",
        bool placementEligible = true,
        string contentTier = "demo",
        int? stimulusId = null)
    {
        var topicId = await db.Topics.Where(t => t.Code == topicCode).Select(t => t.TopicId).SingleAsync();
        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = level,
            ItemType = modality == "listening" ? "short_exchange" : "passage_comprehension",
            // #250: this fixture only authors objective items; a productive one would be a free
            // response and is never built here.
            RenderFormat = "mcq",
            PlacementEligible = placementEligible,
            ContentTier = contentTier,
            LifecycleStatus = lifecycle,
            StimulusId = stimulusId,
        };
        if (modality == "writing")
        {
            item.ItemType = "transactional_correspondence";
            // #250: the override above makes this a productive item, so the widget must follow. A
            // writing item drawn as 'mcq' is exactly what CK_Items_Modality_RenderFormat refuses.
            item.RenderFormat = "free_response";
        }

        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = $"Item {item.ItemId}",
            OptionA = "A",
            OptionB = "B",
            OptionC = "C",
            OptionD = "D",
            KeyLabel = "A",
        });
        await db.SaveChangesAsync();
        return item.ItemId;
    }

    /// <summary>Fills one band of one skill with fresh placement-eligible stock.</summary>
    private static async Task<List<int>> SeedBandAsync(
        CompanionDbContext db, string modality, string topicCode, CefrLevel level, int count)
    {
        var ids = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            ids.Add(await SeedItemAsync(db, modality, topicCode, level));
        }

        return ids;
    }

    private static async Task<int> SeedLearnerAsync(CompanionDbContext db, int certId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == "learner");
        if (role is null)
        {
            role = new Role { Code = "learner", Name = "Learner" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@test.local",
            PasswordHash = [1],
            ActiveCertId = certId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>Records that the learner has answered these items in a practice drill, which is what
    /// "seen" means to the cascade.</summary>
    private static async Task MarkSeenAsync(CompanionDbContext db, int userId, int certId, IEnumerable<int> itemIds)
    {
        var ids = itemIds.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var itemVersions = await db.ItemVersions
            .Where(v => ids.Contains(v.ItemId))
            .ToDictionaryAsync(v => v.ItemId, v => v.ItemVersionId);
        var modality = await db.Items.Where(i => i.ItemId == ids[0]).Select(i => i.Modality).SingleAsync();

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            TopicId = await db.Items.Where(i => i.ItemId == ids[0]).Select(i => i.TopicId).SingleAsync(),
            CefrLevel = await db.Items.Where(i => i.ItemId == ids[0]).Select(i => i.CefrLevel).SingleAsync(),
            Modality = modality,
            QuestionCount = ids.Count,
            Status = "finished",
            CorrectCount = 0,
            QuestionSource = "all",
            StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
            FinishedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        var order = 1;
        foreach (var itemId in ids)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                ItemId = itemId,
                ItemVersionId = itemVersions[itemId],
                SelectedLabel = "A",
                IsCorrect = false,
                DisplayOrder = order++,
                AnsweredAt = DateTime.UtcNow.AddDays(-30),
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Each_objective_skill_draws_the_routed_pair_and_the_productive_skill_draws_nothing()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            // Listening routes low (A1-A2), reading routes high (C1-C2): one pool, two pairs.
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.C1, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.C2, 6);
            // Writing has stock, so "nothing" can only come from the objective-only rule.
            await SeedItemAsync(db, "writing", "W-ARG", CefrLevel.B1);

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int>
                {
                    [skillIds["LIST"]] = 1,
                    [skillIds["READ"]] = 8,
                    [skillIds["WRIT"]] = 4,
                });

            Assert.Equal(2, draw.Skills.Count);
            Assert.DoesNotContain(draw.Skills, skill => skill.Modality == "writing");
            Assert.True(draw.IsComplete);
            Assert.False(draw.IsRefused);

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            Assert.Equal("A1_A2", listening.Route.Code);
            Assert.Equal(6, listening.Items.Count(item => item.Level == CefrLevel.A1));
            Assert.Equal(6, listening.Items.Count(item => item.Level == CefrLevel.A2));

            var reading = Assert.Single(draw.Skills, skill => skill.Modality == "reading");
            Assert.Equal("C1_C2", reading.Route.Code);
            Assert.Equal(6, reading.Items.Count(item => item.Level == CefrLevel.C1));
            Assert.Equal(6, reading.Items.Count(item => item.Level == CefrLevel.C2));

            // Every served item names the version it will render: an item with no version is an item the
            // sitting cannot show.
            Assert.All(draw.Skills, skill => Assert.All(skill.Items, item => Assert.NotNull(item.ItemVersionId)));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Criterion 3, in the currency the ticket asks for: a band that cannot supply enough items refuses
    /// the sitting <b>before any attempt row exists</b>. The trap is a refusal raised at start time, after
    /// <c>CreateAttemptAsync</c> has inserted; the assertion is on the row count.
    ///
    /// <b>The input that would pass a weaker test:</b> an exception message that names one band by luck -
    /// here both the missing band and its kept neighbour are asserted, and the neighbour must not appear
    /// as missing.
    /// </summary>
    [Fact]
    public async Task A_band_that_cannot_supply_five_items_refuses_and_names_the_cell_with_no_attempt_row()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 2);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);

            // A published placement paper, so the counter below can be shown to see an attempt row.
            db.Exams.Add(new Exam
            {
                CertId = certId,
                Name = "Placement",
                Status = "published",
                Purpose = "placement",
                DurationMinutes = 30,
            });
            await db.SaveChangesAsync();

            var service = new PlacementStageTwoDrawService(db);
            var draw = await service.DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 });

            Assert.True(draw.IsRefused);
            // Every objective skill is routed, so both are named: the listening band that is short, and
            // the reading skill that has no stock at all. A refusal that named only one would leave a
            // learner starting a paper one of whose skills cannot be measured.
            var listeningDeficit = Assert.Single(
                draw.Refusals.Where(refusal => refusal.SkillCode == "LIST").ToList());
            Assert.Equal(CefrLevel.A1, listeningDeficit.Level);
            Assert.Equal((6, 2), (listeningDeficit.Wanted, listeningDeficit.Drawn));
            Assert.Equal(4, listeningDeficit.Missing);
            Assert.Contains(draw.Refusals, refusal => refusal.SkillCode == "READ");

            var thrown = await Assert.ThrowsAsync<PlacementException>(() => service.EnsureSittableAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 }));

            Assert.Equal(PlacementException.PlacementStockShort, thrown.Code);
            // The message names the cell: the skill, the band and how many it is short.
            Assert.Contains("LIST at A1 has 2 of 6", thrown.Message);
            Assert.Contains("READ at", thrown.Message);
            Assert.Contains("no sitting was started", thrown.Message);

            // Nothing was created: the refusal is a read, not a start that rolled back.
            Assert.Equal(0, await db.ExamAttempts.CountAsync());
            Assert.Equal(0, await db.PlacementTests.CountAsync());

            // The counter above must be able to see a row, or "zero attempts" proves nothing. Starting
            // the sitting through the real path puts one there, and the preflight-style read that
            // follows still adds none.
            await using (var start = DbContextFactory.Create(connectionString))
            {
                var info = await new PlacementService(start, new LearnerService(start))
                    .StartPlacementTestAsync(userId, await start.Exams
                        .Where(e => e.CertId == certId)
                        .Select(e => e.ExamId)
                        .FirstAsync());
                Assert.True(info.AttemptId > 0);
            }

            Assert.Equal(1, await db.ExamAttempts.CountAsync());
            Assert.Equal(1, await db.PlacementTests.CountAsync());

            // ... and the refusing read changes neither, so a caller that honours it cannot half-start.
            await Assert.ThrowsAsync<PlacementException>(() => service.EnsureSittableAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 }));
            Assert.Equal(1, await db.ExamAttempts.CountAsync());
            Assert.Equal(1, await db.PlacementTests.CountAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Criterion 5: five of a six-item band still runs, and the draw records that it did - the per-band
    /// counts say asked-for 6, drawn 5 - without repeating an item to reach six.
    ///
    /// <b>The input that would pass a weaker test:</b> a form that repeats one A2 item twice; the item
    /// count is then 6 and every band looks full.
    /// </summary>
    [Fact]
    public async Task A_five_of_six_band_runs_a_degraded_form_and_records_the_counts()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 5);
            // Reading is routed too; give it a full pair so the refusal cannot be what this test sees.
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A2, 6);

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 });

            Assert.False(draw.IsRefused);
            Assert.True(draw.IsDegraded);
            // The record names the cell and both counts, not just "a shorter paper".
            Assert.Equal("LIST A2 5/6", draw.DegradedDescription);

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            var a2 = Assert.Single(listening.Bands, band => band.Level == CefrLevel.A2);
            Assert.Equal(6, a2.Wanted);
            Assert.Equal(5, a2.Drawn);

            Assert.Equal(11, listening.Items.Count);
            Assert.Equal(11, listening.Items.Select(item => item.ItemId).Distinct().Count());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The band boundary at the query, not only in the arithmetic: an over-full neighbour must never
    /// top up a short band. A1 asks for six and holds two; A2 holds thirty.
    /// </summary>
    [Fact]
    public async Task An_over_full_neighbour_never_tops_up_a_short_band()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 2);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 30);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A2, 6);

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 });

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            Assert.Equal(2, listening.Items.Count(item => item.Level == CefrLevel.A1));
            Assert.Equal(6, listening.Items.Count(item => item.Level == CefrLevel.A2));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The filters stage one already enforces apply to stage two's primary tiers unchanged: quarantined
    /// and other-tier stock is never drawn at all. Practice-only stock is the one filter that differs
    /// between the stages - stage one has no reserve tier and stage two does - so it may be served only
    /// at <see cref="PlacementStageTwoTier.PracticeReserve"/>.
    ///
    /// <b>The input that would pass a weaker test:</b> a query with no lifecycle filter serves the
    /// quarantined items - lowest ids here - as primary stock, and every band still fills. The id
    /// assertions make that a named failure.
    /// </summary>
    [Fact]
    public async Task Quarantined_and_other_tier_stock_is_never_drawn_and_practice_stock_only_from_the_reserve()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            var wrong = new List<int>();
            var practiceOnly = new List<int>();
            for (var i = 0; i < 6; i++)
            {
                wrong.Add(await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, lifecycle: "quarantined"));
                wrong.Add(await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A2, contentTier: "production"));
                practiceOnly.Add(await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A2, placementEligible: false));
            }

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 6);

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0 });

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            Assert.DoesNotContain(listening.Items, item => wrong.Contains(item.ItemId));

            // A2 is filled entirely from the reserve, and every one of those items says so.
            var a2 = listening.Items.Where(item => item.Level == CefrLevel.A2).ToList();
            Assert.Equal(6, a2.Count);
            Assert.All(a2, item => Assert.Contains(item.ItemId, practiceOnly));
            Assert.All(a2, item => Assert.Equal(PlacementStageTwoTier.PracticeReserve, item.Tier));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The reserve is a source at the same band and nowhere else: A1 holds nothing while A2 holds twelve
    /// practice items, and A1 is still short. A reserve that was not band-scoped would fill it.
    /// </summary>
    [Fact]
    public async Task Practice_reserve_at_a_neighbouring_band_never_tops_up_a_short_band()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            for (var i = 0; i < 12; i++)
            {
                await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A2, placementEligible: false);
            }

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);

            var service = new PlacementStageTwoDrawService(db);
            var draw = await service.DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0 });

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            var a1 = Assert.Single(listening.Bands, band => band.Level == CefrLevel.A1);
            Assert.Equal((6, 0), (a1.Wanted, a1.Drawn));
            Assert.DoesNotContain(listening.Items, item => item.Level == CefrLevel.A1);

            var a2 = listening.Items.Where(item => item.Level == CefrLevel.A2).ToList();
            Assert.Equal(6, a2.Count);
            Assert.Equal(6, a2.Select(item => item.ItemId).Distinct().Count());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Tier two of the cascade at the query: the practice reserve is reached only after the band's
    /// placement-eligible stock is used up. The trap is a query with no <c>PlacementEligible</c> column at
    /// all - it fills A2 from the reserve while eligible stock still exists.
    /// </summary>
    [Fact]
    public async Task The_practice_reserve_is_used_only_after_the_eligible_stock()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            var eligible = await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 3);
            var reserve = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                reserve.Add(await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A2, placementEligible: false));
            }

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0 });

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            var a1 = listening.Items.Where(item => item.Level == CefrLevel.A1).ToList();
            Assert.All(a1, item => Assert.Contains(item.ItemId, eligible));
            Assert.All(a1, item => Assert.Equal(PlacementStageTwoTier.Primary, item.Tier));

            var a2 = listening.Items.Where(item => item.Level == CefrLevel.A2).ToList();
            Assert.Equal(6, a2.Count);
            Assert.Equal(3, a2.Count(item => item.Tier == PlacementStageTwoTier.Primary));
            Assert.Equal(3, a2.Count(item => item.Tier == PlacementStageTwoTier.PracticeReserve));
            Assert.All(a2.Where(item => item.Tier == PlacementStageTwoTier.PracticeReserve),
                item => Assert.Contains(item.ItemId, reserve));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// "Rather than repeating an item" inside one administration: the stage-one roster is passed in, and
    /// none of those items may come back at the band stage two measures. The trap is a draw that has no
    /// knowledge of stage one - it re-serves an item the learner answered ten minutes ago.
    /// </summary>
    [Fact]
    public async Task An_item_stage_one_already_served_is_not_served_again()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            var a1 = await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 7);
            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A1, 6);
            await SeedBandAsync(db, "reading", "R-GIST", CefrLevel.A2, 6);

            // Stage one served the first A1 item of the same skill.
            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0, [skillIds["READ"]] = 0 },
                servedItemIds: [a1[0]]);

            var listening = Assert.Single(draw.Skills, skill => skill.Modality == "listening");
            Assert.DoesNotContain(listening.Items, item => item.ItemId == a1[0]);
            Assert.Equal(6, listening.Items.Count(item => item.Level == CefrLevel.A1));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The seen-history the cascade reads is the learner's own. A band whose only six items ANOTHER
    /// learner has answered is still fully available here, and the reverse: this learner's seen items are
    /// recycled rather than served as fresh.
    /// </summary>
    [Fact]
    public async Task Seen_history_is_the_learners_own_and_recycles_before_it_repeats()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var learner = await SeedLearnerAsync(db, certId);
            var other = await SeedLearnerAsync(db, certId);

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A1, 6);
            var a2 = await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);
            // Another learner has answered every A2 item; this learner has answered none.
            await MarkSeenAsync(db, other, certId, a2);

            var service = new PlacementStageTwoDrawService(db);
            var fresh = await service.DrawAsync(
                learner, certId, "demo", new Dictionary<int, int> { [skillIds["LIST"]] = 0 });
            var freshA2 = fresh.Skills.Single(s => s.Modality == "listening").Items
                .Where(item => item.Level == CefrLevel.A2);
            Assert.All(freshA2, item => Assert.Equal(PlacementStageTwoTier.Primary, item.Tier));

            // This learner now answers every A2 item too, so the same band must recycle, not repeat.
            await MarkSeenAsync(db, learner, certId, a2);
            var recycled = await service.DrawAsync(
                learner, certId, "demo", new Dictionary<int, int> { [skillIds["LIST"]] = 0 });
            var recycledA2 = recycled.Skills.Single(s => s.Modality == "listening").Items
                .Where(item => item.Level == CefrLevel.A2).ToList();
            Assert.Equal(6, recycledA2.Count);
            Assert.All(recycledA2, item => Assert.Equal(PlacementStageTwoTier.RecycledOldestSeen, item.Tier));
            Assert.Equal(6, recycledA2.Select(item => item.ItemId).Distinct().Count());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// D7's partial-stimulus requirement at the six-item count stage two asks for: 6 at A1 assembled
    /// from a three-child and a four-child stimulus is 3 + 3, a strict subset of the second bundle.
    ///
    /// <b>The input that would pass a weaker test:</b> a bundle-atomic draw returns three items, or
    /// seven. The strict-subset assertion is the one it cannot satisfy.
    /// </summary>
    [Fact]
    public async Task A_six_item_band_is_served_as_a_subset_of_shared_stimuli()
    {
        var (connectionString, name) = await FreshAsync();
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var (certId, skillIds) = await SeedCertificateAsync(db);
            var userId = await SeedLearnerAsync(db, certId);

            db.Stimuli.AddRange(
                new Stimulus { CefrLevel = CefrLevel.A1, Passage = "Three.", DomainContext = "general" },
                new Stimulus { CefrLevel = CefrLevel.A1, Passage = "Four.", DomainContext = "general" });
            await db.SaveChangesAsync();
            var stimulusIds = await db.Stimuli.OrderBy(s => s.StimulusId).Select(s => s.StimulusId).ToListAsync();

            for (var i = 0; i < 3; i++)
            {
                await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, stimulusId: stimulusIds[0]);
            }

            for (var i = 0; i < 4; i++)
            {
                await SeedItemAsync(db, "listening", "L-GIST", CefrLevel.A1, stimulusId: stimulusIds[1]);
            }

            await SeedBandAsync(db, "listening", "L-GIST", CefrLevel.A2, 6);

            var draw = await new PlacementStageTwoDrawService(db).DrawAsync(
                userId,
                certId,
                "demo",
                new Dictionary<int, int> { [skillIds["LIST"]] = 0 });

            var a1 = draw.Skills.Single(s => s.Modality == "listening").Items
                .Where(item => item.Level == CefrLevel.A1).ToList();
            Assert.Equal(6, a1.Count);
            Assert.Equal(3, a1.Count(item => item.StimulusId == stimulusIds[0]));
            // Three of the four children of the second bundle: never the whole bundle.
            Assert.Equal(3, a1.Count(item => item.StimulusId == stimulusIds[1]));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
