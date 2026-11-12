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
/// #276: <b>a live placement sitting is built by the stage-one draw, and stage two is entered where
/// stage one ends.</b>
/// <para>
/// <b>Why this test exists.</b> <c>PlacementStageOneDrawTests</c> and <c>PlacementStageTwoDrawTests</c>
/// drive the two draw services directly, so they prove the draws work and say nothing about whether
/// anything runs them. That gap is the whole ticket: the draws were complete, tested, and executed by
/// nothing for two tickets. So every assertion here is about a REAL
/// <c>PlacementService.StartPlacementTestAsync</c> — the learner-facing call — and about the rows it
/// leaves on the attempt.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "The attempt has questions" passes on the static
/// paper, which is exactly what a failed wiring also produces. So the assertions are on <b>which</b>
/// positions: the served paper must be one per band per objective skill (1/1/2/2/1/1), and none of them may
/// be a position the exam's own roster binds. The static paper is a contiguous subset of the roster, so a
/// sitting that fell back has every position in that set and fails on the first band it lacks.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementDrawWiringTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A certificate whose two objective skills have full stage-one stock, plus a static placement paper
    /// over DIFFERENT items. The static paper is the fallback, and it is deliberately bad in the way the
    /// ticket describes: two bands only, nothing at A1/C1/C2, so a sitting that fell back cannot pass a
    /// six-band assertion.
    /// </summary>
    private sealed record Fixture(
        int CertId,
        int ExamId,
        IReadOnlyDictionary<string, int> SkillIds,
        IReadOnlyList<int> RosterItemIds,
        IReadOnlyList<int> StageOneItemIds);

    private async Task<Fixture> SeedAsync(CompanionDbContext db, string code)
    {
        db.Certificates.Add(new Certificate { Code = code, Name = $"Cert {code}", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 });
        await db.SaveChangesAsync();

        // The closed twenty-topic vocabulary the schema enforces: L-DET / R-DET are the two this
        // certificate's objective skills draw from.
        db.Topics.AddRange(
            new Topic { Modality = "listening", Code = "L-DET", Name = "Listening for detail" },
            new Topic { Modality = "reading", Code = "R-DET", Name = "Reading for detail" });
        await db.SaveChangesAsync();

        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        db.Sections.AddRange(
            new Section { SkillId = skillIds["LIST"], CertId = certId, Modality = "listening", Code = "L1", Name = "Listening", ExpectedQuestionCount = 8, DisplayOrder = 1, NavigationMode = "FULL_SANDBOX" },
            new Section { SkillId = skillIds["READ"], CertId = certId, Modality = "reading", Code = "R1", Name = "Reading", ExpectedQuestionCount = 8, DisplayOrder = 2, NavigationMode = "FULL_SANDBOX" });
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand { CertId = certId, Code = "A2", Name = "Elementary", MinTotal = 0, MaxTotal = 990, DisplayOrder = 1 });
        db.CefrCutScoreVersions.Add(new CefrCutScoreVersion
        {
            CertId = certId, TableVersion = 1, ReportsSkillsSeparately = false,
            Source = "fixture", PublishedAt = new DateOnly(2026, 1, 1),
        });
        foreach (var skillId in skillIds.Values)
        {
            db.CefrCutScores.Add(new CefrCutScore
            {
                CertId = certId, TableVersion = 1, SkillId = skillId,
                CefrLevel = "A2", RowKind = "cut", MinScaled = 0, MaxScaled = 500,
            });
        }

        var exam = new Exam
        {
            CertId = certId, Name = $"Placement {code}", Status = "published",
            DurationMinutes = 45, Purpose = "placement", QuestionCount = 24, ContentTier = "demo",
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var sectionOf = await db.Sections
            .Where(s => s.CertId == certId)
            .ToDictionaryAsync(s => s.Modality, s => s.SectionId);

        // The static paper's own roster: eight positions, two bands, nothing at A1/C1/C2. A sitting that
        // falls back serves THIS, so it is a contiguous subset of what the fallback can render.
        var rosterItemIds = new List<int>();
        foreach (var (modality, sectionId) in sectionOf)
        {
            foreach (var (level, order) in new[] { ("A2", 1), ("A2", 2), ("B1", 3), ("B1", 4) })
            {
                var position = await PoolFixture.AddMcqAsync(
                    db, exam.ExamId, certId, sectionId, modality,
                    $"Static {modality} {level} {order}", displayOrder: order, level: level);
                rosterItemIds.Add(position.ItemId);
            }
        }

        // Stage-one stock: the blueprint's own distribution, per objective skill, on items the exam does
        // NOT bind. Each is placement-eligible and active, which is what the draw queries.
        var stageOneItemIds = new List<int>();
        foreach (var (modality, sectionId) in sectionOf)
        {
            var order = 5;
            foreach (var (level, count) in PlacementStageOneBlueprint.Distribution)
            {
                for (var index = 0; index < count; index++)
                {
                    db.Items.Add(new Item
                    {
                        Modality = modality,
                        TopicId = await db.Topics.Where(t => t.Modality == modality).Select(t => t.TopicId).FirstAsync(),
                        CefrLevel = level,
                        ItemType = modality == "listening" ? "short_exchange" : "sentence_cloze",
                        RenderFormat = "mcq",
                        PlacementEligible = true,
                        ContentTier = "demo",
                        LifecycleStatus = "active",
                    });
                    await db.SaveChangesAsync();

                    var itemId = await db.Items.OrderByDescending(i => i.ItemId).Select(i => i.ItemId).FirstAsync();
                    db.ItemVersions.Add(new ItemVersion
                    {
                        ItemId = itemId, VersionNumber = 1, Stem = $"StageOne {modality} {level} {index}",
                        OptionA = "A", OptionB = "B", OptionC = "C", OptionD = "D", KeyLabel = "A",
                    });
                    await db.SaveChangesAsync();

                    // Bound to the paper, because AttemptItemResponses names a roster position: a draw
                    // that selected an unbound item could not serve it at all.
                    db.ExamItems.Add(new ExamItem
                    {
                        ExamId = exam.ExamId, SectionId = sectionId, CertId = certId,
                        ItemId = itemId,
                        ItemVersionId = await db.ItemVersions.Where(v => v.ItemId == itemId).Select(v => v.ItemVersionId).SingleAsync(),
                        Modality = modality, DisplayOrder = order++, UnitNumber = 1,
                    });
                    await db.SaveChangesAsync();
                    stageOneItemIds.Add(itemId);
                }
            }
        }

        return new Fixture(certId, exam.ExamId, skillIds, rosterItemIds, stageOneItemIds);
    }

    private static async Task<int> SeedLearnerAsync(CompanionDbContext db, int certId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == "learner-276");
        if (role is null)
        {
            role = new Role { Code = "learner-276", Name = "Learner 276" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = role.RoleId, Email = email, PasswordHash = [0x01], ActiveCertId = certId });
        await db.SaveChangesAsync();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Criterion 1 and 4: the sitting is the DRAW, not the static paper.
    /// </summary>
    [Fact]
    public async Task A_live_sitting_is_built_by_stage_one_one_item_per_band_per_objective_skill()
    {
        var name = $"EE276Draw_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var fixture = await SeedAsync(db, Unique("P276"));
            var userId = await SeedLearnerAsync(db, fixture.CertId);

            var service = new PlacementService(db, new LearnerService(db));
            var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);

            await using var read = DbContextFactory.Create(connectionString);

            // The paper the attempt was served, in sitting order.
            var served = await read.AttemptItemResponses.AsNoTracking()
                .Where(r => r.AttemptId == info.AttemptId)
                .OrderBy(r => r.PositionNumber)
                .Select(r => r.ExamItemId)
                .ToListAsync();

            // 8 per objective skill, 2 skills. The static paper has 16 positions too, so the COUNT alone
            // is not the assertion - the bands and the identity of the items are.
            Assert.Equal(16, served.Count);

            // Position numbers are 1-based and contiguous: a gap leaves the navigator a hole.
            var numbers = await read.AttemptItemResponses.AsNoTracking()
                .Where(r => r.AttemptId == info.AttemptId)
                .OrderBy(r => r.PositionNumber)
                .Select(r => r.PositionNumber)
                .ToListAsync();
            Assert.Equal(Enumerable.Range(1, 16), numbers);

            // Which items, by band and skill. The draw's own distribution is the expectation, so this
            // cannot pass on a paper that served every item at B1, and it cannot pass on the static paper
            // - whose roster holds A2 and B1 only.
            var servedItems = await read.ExamItems.AsNoTracking()
                .Where(i => served.Contains(i.ExamItemId))
                .Join(read.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId,
                    (i, item) => new { i.Modality, item.CefrLevel })
                .ToListAsync();

            foreach (var modality in new[] { "listening", "reading" })
            {
                var forSkill = servedItems.Where(i => i.Modality == modality).ToList();
                Assert.Equal(8, forSkill.Count);
                foreach (var (level, count) in PlacementStageOneBlueprint.Distribution)
                {
                    Assert.True(
                        forSkill.Count(i => i.CefrLevel == level) == count,
                        $"{modality} served {forSkill.Count(i => i.CefrLevel == level)} item(s) at "
                        + $"{level.Code}; the stage-one blueprint asks for {count}");
                }
            }

            // Criterion 4, stated directly: the static paper is NOT what the learner sits. Its roster
            // items are the ones the fallback would serve, and a stage-one draw never picks one.
            Assert.Empty(served.Intersect(fixture.RosterItemIds));

            // Every served position is one the draw chose, not merely "some position".
            var servedItemIds = await read.ExamItems.AsNoTracking()
                .Where(i => served.Contains(i.ExamItemId))
                .Select(i => i.ItemId)
                .ToListAsync();
            Assert.Equal(16, servedItemIds.Intersect(fixture.StageOneItemIds).Count());

            // Stage one's own record: one bundle per objective skill, stage 1, written by the one writer.
            var bundles = await read.AttemptStageBundles.AsNoTracking()
                .Where(b => b.AttemptId == info.AttemptId)
                .ToListAsync();
            Assert.Equal(2, bundles.Count);
            Assert.All(bundles, bundle =>
            {
                Assert.Equal(1, bundle.StageNumber);
                Assert.Equal(DerivedFactWriters.AttemptPaper, bundle.WrittenByService);
                Assert.Equal(CefrLevel.A1, bundle.BandLower);
                Assert.Equal(CefrLevel.C2, bundle.BandUpper);
            });
            Assert.Equal(
                fixture.SkillIds.Values.OrderBy(id => id),
                bundles.Select(b => b.SkillId).OrderBy(id => id));

            // Criterion 5: placement is free on this path too.
            Assert.Empty(await read.CreditTransactions.Where(t => t.UserId == userId).ToListAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The constraint the ticket is explicit about: <b>a draw that cannot complete must not turn a
    /// sitting into a 500.</b> The fixture starves every band but B1, so stage one cannot complete and the
    /// exam's own paper is served instead - which is what every learner got before this ticket.
    /// </summary>
    [Fact]
    public async Task A_draw_that_cannot_complete_falls_back_to_the_exam_paper_instead_of_refusing()
    {
        var name = $"EE276Fall_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var fixture = await SeedAsync(db, Unique("P276F"));
            var userId = await SeedLearnerAsync(db, fixture.CertId);

            // Stage one is short at five of six bands: no A1, no A2, no B2, no C1, no C2.
            await db.Database.ExecuteSqlAsync($"""
                UPDATE Items SET LifecycleStatus = 'quarantined'
                WHERE ItemId IN (SELECT ItemId FROM Items WHERE PlacementEligible = 1 AND CefrLevel <> 'B1')
                """);

            var service = new PlacementService(db, new LearnerService(db));
            var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);

            await using var read = DbContextFactory.Create(connectionString);

            // No 500, and a real sitting: this is the regression the ticket names.
            Assert.True(info.PlacementId > 0);
            Assert.True(info.AttemptId > 0);

            // The fallback is the shared roster, and it wrote no served paper - the two branches are
            // distinguishable, which is what makes "we fell back" a fact rather than an assumption.
            Assert.Empty(await read.AttemptItemResponses.Where(r => r.AttemptId == info.AttemptId).ToListAsync());
            Assert.Empty(await read.AttemptStageBundles.Where(b => b.AttemptId == info.AttemptId).ToListAsync());

            // … and the sitting still renders the exam's own paper, so the learner can actually sit it.
            // The count is the exam's roster, read rather than hard-coded: this fixture binds the
            // stage-one items too (they must be positions of this paper), so the fallback paper is the
            // whole roster rather than only the eight "static" ones.
            var take = await service.GetPlacementTakeAsync(userId, info.PlacementId, null);
            Assert.Equal(
                await read.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId),
                take.TotalQuestions);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Criterion 2: stage two is reachable at its boundary and the routed pair is recorded on the attempt.
    /// <para>
    /// The boundary is the skill advance, so the test drives the real one: answer the Listening stage-one
    /// items, then request the first Reading question, which is what
    /// <c>PlacementService.AdvanceAtLockBoundaryAsync</c> treats as "leave Listening".
    /// </para>
    /// </summary>
    [Fact]
    public async Task Leaving_a_skill_records_the_band_pair_stage_one_routed_it_to()
    {
        var name = $"EE276Route_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            int listeningSkillId;
            int placementId;
            int attemptId;
            int readingFirstOrder;
            List<(int QuestionId, int OptionId)> listeningAnswers;

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var fixture = await SeedAsync(db, Unique("P276R"));
                var userId = await SeedLearnerAsync(db, fixture.CertId);
                listeningSkillId = fixture.SkillIds["LIST"];

                var service = new PlacementService(db, new LearnerService(db));
                var info = await service.StartPlacementTestAsync(userId, fixture.ExamId);
                placementId = info.PlacementId;
                attemptId = info.AttemptId;

                var take = await service.GetPlacementTakeAsync(userId, placementId, null);
                var listening = take.Questions!
                    .Where(q => q.SkillCode == "LIST")
                    .OrderBy(q => q.DisplayOrder)
                    .ToList();
                Assert.Equal(PlacementStageOneBlueprint.ItemsPerObjectiveSkill, listening.Count);

                readingFirstOrder = take.Questions!
                    .Where(q => q.SkillCode == "READ")
                    .Min(q => q.DisplayOrder);

                // Every stage-one Listening answer correct, so the score routes high and the pair is
                // deterministic: 8 correct is the top row of D7's table.
                listeningAnswers = listening
                    .Select(q => (q.QuestionId, q.Options!.Single(o => o.Label == "A").OptionId))
                    .ToList();

                foreach (var (questionId, optionId) in listeningAnswers)
                {
                    await service.SaveAnswerAsync(userId, placementId, questionId, optionId, isFlagged: false);
                }
            }

            // The boundary: asking for Reading's first question is the leave-Listening request.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var service = new PlacementService(db, new LearnerService(db));
                await service.GetPlacementTakeAsync(userId: await CurrentUserIdAsync(db, placementId), placementId, readingFirstOrder);
            }

            await using var read = DbContextFactory.Create(connectionString);

            // The pair is recorded on the attempt, for the skill that just closed, and by the one writer.
            var stageTwo = await read.AttemptStageBundles.AsNoTracking()
                .SingleAsync(b => b.AttemptId == attemptId && b.SkillId == listeningSkillId && b.StageNumber == 2);

            Assert.Equal(DerivedFactWriters.AttemptPaper, stageTwo.WrittenByService);

            // 8 of 8 routes to the top row, so the pair is C1_C2 - the route is a pure function of the
            // score, and asserting the exact pair is what proves the score was read rather than guessed.
            var expected = PlacementStageTwoBlueprint.Route(listeningAnswers.Count);
            Assert.Equal(expected.Lower, stageTwo.BandLower);
            Assert.Equal(expected.Upper, stageTwo.BandUpper);

            // Reading has not been left, so it has no stage-two row: the boundary is per skill.
            Assert.DoesNotContain(
                await read.AttemptStageBundles.AsNoTracking().Where(b => b.AttemptId == attemptId).ToListAsync(),
                b => b.StageNumber == 2 && b.SkillId != listeningSkillId);

            // Criterion 5 again: routing wrote no ledger row.
            Assert.Empty(await read.CreditTransactions.Where(t => t.AttemptId == attemptId).ToListAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    private static Task<int> CurrentUserIdAsync(CompanionDbContext db, int placementId) =>
        db.PlacementTests.Where(p => p.PlacementId == placementId).Select(p => p.UserId).SingleAsync();
}
