using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #273: <b>the Studio's group controls write to a paper that exists.</b>
/// <para>
/// The screen rendered a pool paper correctly and then made three writes to routes #265 deleted:
/// <c>PUT /admin/exams/1/studio/group/1</c> answered 404, so a passage edit looked saved and was not.
/// This file pins the two writes that replace it, and - as importantly - what they must not touch: the
/// pool's items, their versions, and any other paper that binds them.
/// </para>
/// <para>
/// Every assertion is made against a RE-READ through the Studio's own read path, because the defect was
/// that the write and the read disagreed about which table held the group.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StudioGroupWriterTests(DatabaseFixture database)
{
    /// <summary>A passage shared by two positions, plus one solo position with no stimulus.</summary>
    private sealed record Fixture(
        int CertId, int ExamId, int SectionId,
        int StimulusId, int[] BundlePositions, int[] SoloPosition);

    [Fact]
    public async Task A_groups_chrome_and_unit_persist_and_the_edit_survives_a_reread()
    {
        var name = $"EE273W_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);

            var before = await service.GetExamStudioAsync(fixture.ExamId);
            var group = before.Sections.Single(s => s.SectionId == fixture.SectionId).Groups
                .Single(g => g.GroupId == fixture.StimulusId);
            Assert.Equal(2, group.Questions.Count);

            // The precondition the whole ticket rests on: this paper has no legacy rows at all, so a
            // writer that lands on one writes to nothing and the screen never changes. #269 dropped the
            // legacy tables, so that absence is a schema fact; the writer's target being real is what
            // the re-read below proves.

            await service.UpdateExamStudioGroupAsync(
                fixture.ExamId,
                fixture.StimulusId,
                new UpdateQuestionGroupRequest(
                    SectionId: fixture.SectionId,
                    Passage: "The depot notice now reads six o'clock.",
                    Transcript: "A short announcement about opening hours.",
                    // A slice needs a track to be a window of: the read resolves the pair through
                    // ExamAudio, and a slice with no path is a window nobody can play.
                    AudioPath: "/media/exams/9/section-master.mp3",
                    ImagePath: "/media/exams/9/depot.jpg",
                    ImageAlt: "A depot door with an hours card.",
                    AudioStartMs: 12000,
                    AudioEndMs: 47500,
                    PrepSeconds: 5,
                    ResponseSeconds: 90,
                    UnitNumber: 2));

            // The one that was broken: the re-read must show the edit. The old writer landed on a table
            // the reader does not consult, so this read returned the pre-edit text.
            var after = (await service.GetExamStudioAsync(fixture.ExamId))
                .Sections.Single(s => s.SectionId == fixture.SectionId).Groups
                .Single(g => g.GroupId == fixture.StimulusId);

            Assert.Equal("The depot notice now reads six o'clock.", after.Passage);
            Assert.Equal("A short announcement about opening hours.", after.Transcript);
            Assert.Equal("A depot door with an hours card.", after.ImageAlt);
            Assert.NotNull(after.ImagePath);
            Assert.Equal(12000, after.AudioStartMs);
            Assert.Equal(47500, after.AudioEndMs);
            Assert.Equal(90, after.ResponseSeconds);

            // The unit is the paper's own, so it is written on every position of the bundle.
            Assert.Equal(2, after.UnitNumber);
            await using var verify = DbContextFactory.Create(connectionString);
            Assert.All(
                await verify.ExamItems.AsNoTracking().Where(i => fixture.BundlePositions.Contains(i.ExamItemId)).ToListAsync(),
                position => Assert.Equal(2, position.UnitNumber));

            // Content is pool stock, not a paper copy: the same rows are what a sibling paper reads.
            var stimulus = await verify.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == fixture.StimulusId);
            Assert.Equal("The depot notice now reads six o'clock.", stimulus.Passage);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The locks are what a unit edit is FOR, so the sitting must follow the write: a position moved
    /// into unit 2 is locked while the attempt is still in unit 1, and open once it transitions.
    /// </summary>
    [Fact]
    public async Task Changing_a_groups_unit_moves_the_sitting_lock_with_it()
    {
        var name = $"EE273U_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString, sectionUnitCount: 2);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);
            var userId = await SeedLearnerAsync(db, fixture.CertId);

            var engine = new AttemptEngine(db);
            var attempt = await engine.CreateAttemptAsync(userId, fixture.CertId, fixture.ExamId, 60);
            Assert.Equal(1, attempt.ActiveUnitNumber);

            var takeBefore = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);
            var lockedBefore = takeBefore.Navigator
                .Where(item => fixture.BundlePositions.Contains(item.QuestionId))
                .ToList();
            Assert.All(lockedBefore, item => Assert.False(item.IsLocked));

            // Move the bundle to unit 2. Unit 1 stays open for the solo position.
            await service.UpdateExamStudioGroupAsync(
                fixture.ExamId,
                fixture.StimulusId,
                new UpdateQuestionGroupRequest(SectionId: fixture.SectionId, Passage: "Moved to stage 2.", UnitNumber: 2));

            var takeAfter = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);
            var lockedAfter = takeAfter.Navigator
                .Where(item => fixture.BundlePositions.Contains(item.QuestionId))
                .ToList();
            Assert.All(lockedAfter, item => Assert.True(item.IsLocked, "unit 2 is not the active unit"));
            Assert.False(
                takeAfter.Navigator.Single(item => item.QuestionId == fixture.SoloPosition[0]).IsLocked,
                "the solo position is still in unit 1, which is active");

            // And the transition the lock exists for opens them.
            var moved = await engine.TransitionUnitAsync(attempt.AttemptId, 2);
            Assert.Equal(2, moved.ActiveUnitNumber);
            var takeUnit2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);
            Assert.All(
                takeUnit2.Navigator.Where(item => fixture.BundlePositions.Contains(item.QuestionId)),
                item => Assert.False(item.IsLocked));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    [Fact]
    public async Task Removing_a_group_removes_the_positions_and_leaves_the_pool_intact()
    {
        var name = $"EE273D_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);

            await service.DeleteExamStudioGroupAsync(fixture.ExamId, fixture.StimulusId);

            var after = (await service.GetExamStudioAsync(fixture.ExamId))
                .Sections.Single(s => s.SectionId == fixture.SectionId);

            // The paper lost the bundle and kept the solo position.
            Assert.DoesNotContain(after.Groups, g => g.GroupId == fixture.StimulusId);
            Assert.Single(after.Groups);
            Assert.Equal(1, after.CurrentQuestionCount);

            await using var verify = DbContextFactory.Create(connectionString);
            Assert.Equal(0, await verify.ExamItems.CountAsync(i => fixture.BundlePositions.Contains(i.ExamItemId)));

            // Never the stock: the items, their versions and the stimulus they share are pool rows a
            // second paper may still be sitting on.
            Assert.Equal(2, await verify.Items.CountAsync(i => i.StimulusId == fixture.StimulusId));
            Assert.Equal(3, await verify.ItemVersions.CountAsync());
            Assert.Equal(1, await verify.Stimuli.CountAsync(s => s.StimulusId == fixture.StimulusId));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    [Fact]
    public async Task A_group_holding_an_answered_position_is_refused_by_name()
    {
        var name = $"EE273A_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);
            var userId = await SeedLearnerAsync(db, fixture.CertId);

            var engine = new AttemptEngine(db);
            var attempt = await engine.CreateAttemptAsync(userId, fixture.CertId, fixture.ExamId, 60);
            await engine.SaveAnswerAsync(
                attempt.AttemptId, userId, fixture.BundlePositions[0],
                OptionId(await db.ExamItems.AsNoTracking()
                    .Where(i => i.ExamItemId == fixture.BundlePositions[0])
                    .Select(i => i.ItemVersionId)
                    .SingleAsync()), false);

            var refusal = await Assert.ThrowsAsync<AdminExamException>(
                () => service.DeleteExamStudioGroupAsync(fixture.ExamId, fixture.StimulusId));

            Assert.Equal(ApiError.Codes.QuestionHasAttemptAnswers, refusal.Code);
            Assert.Contains("answered", refusal.Message, StringComparison.OrdinalIgnoreCase);

            await using var verify = DbContextFactory.Create(connectionString);
            Assert.Equal(2, await verify.ExamItems.CountAsync(i => fixture.BundlePositions.Contains(i.ExamItemId)));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The Studio rail draws a solo position as its own group of one, addressed as the negated roster id
    /// (<c>BuildPoolSectionDto</c>). Its unit is editable; its chrome is not, because it has no stimulus.
    /// </summary>
    [Fact]
    public async Task A_solo_position_takes_a_unit_edit_and_refuses_a_stimulus_it_does_not_have()
    {
        var name = $"EE273S_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString, sectionUnitCount: 2);
            var soloId = fixture.SoloPosition[0];
            var soloGroupId = -soloId;

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);

            // What the screen sends for a solo card: no stimulus fields, only the unit.
            await service.UpdateExamStudioGroupAsync(
                fixture.ExamId,
                soloGroupId,
                new UpdateQuestionGroupRequest(SectionId: fixture.SectionId, UnitNumber: 2));

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                Assert.Equal(
                    2,
                    await verify.ExamItems.AsNoTracking()
                        .Where(i => i.ExamItemId == soloId).Select(i => i.UnitNumber).SingleAsync());
            }

            var refusal = await Assert.ThrowsAsync<AdminExamException>(
                () => service.UpdateExamStudioGroupAsync(
                    fixture.ExamId,
                    soloGroupId,
                    new UpdateQuestionGroupRequest(SectionId: fixture.SectionId, Passage: "A passage it cannot hold.")));

            Assert.Equal(ApiError.Codes.Validation, refusal.Code);
            Assert.Contains("no stimulus", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A group address that names no bundle of this paper is refused rather than written to whatever
    /// row shares the integer. The stimulus of ANOTHER paper is the case that matters: a silent match
    /// there would edit a different paper's passage.
    /// </summary>
    [Fact]
    public async Task A_stimulus_bound_to_no_position_of_this_paper_is_refused()
    {
        var name = $"EE273X_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var service = new AdminExamService(db, TimeProvider.System);

            var stranger = new Stimulus { CefrLevel = CefrLevel.B1, Passage = "Another paper's bundle." };
            db.Stimuli.Add(stranger);
            await db.SaveChangesAsync();

            var refusal = await Assert.ThrowsAsync<AdminExamException>(
                () => service.UpdateExamStudioGroupAsync(
                    fixture.ExamId,
                    stranger.StimulusId,
                    new UpdateQuestionGroupRequest(SectionId: fixture.SectionId, Passage: "Rewritten.")));

            Assert.Equal(ApiError.Codes.Validation, refusal.Code);

            await using var verify = DbContextFactory.Create(connectionString);
            Assert.Equal(
                "Another paper's bundle.",
                await verify.Stimuli.AsNoTracking()
                    .Where(s => s.StimulusId == stranger.StimulusId)
                    .Select(s => s.Passage)
                    .SingleAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The Studio is the pool editor. #269 dropped <c>QuestionGroups</c>, so "this needs no such row" is
    /// a schema fact rather than a count; what the test still proves is that the group write reaches the
    /// pool row and the read keeps drawing two groups from the roster.
    /// </summary>
    [Fact]
    public async Task The_studio_group_write_needs_no_question_group_row()
    {
        var name = $"EE273N_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            // #269: QuestionGroups is gone, so this file cannot need one. What it may still assert is
            // that the read draws the same two groups from the roster alone.
            var service = new AdminExamService(db, TimeProvider.System);
            var studio = await service.GetExamStudioAsync(fixture.ExamId);
            Assert.Equal(2, studio.Sections.Single(s => s.SectionId == fixture.SectionId).Groups.Count);

            await service.UpdateExamStudioGroupAsync(
                fixture.ExamId,
                fixture.StimulusId,
                new UpdateQuestionGroupRequest(SectionId: fixture.SectionId, Passage: "Round-tripped.", UnitNumber: 1));

            // The write reached the stimulus the pool group IS, and the read still sees two groups.
            Assert.Equal(
                "Round-tripped.",
                await db.Stimuli.AsNoTracking()
                    .Where(s => s.StimulusId == fixture.StimulusId).Select(s => s.Passage).SingleAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private static int OptionId(int itemVersionId) => itemVersionId * 10 + 1;

    private static async Task<int> SeedLearnerAsync(CompanionDbContext db, int certId)
    {
        var email = $"{Guid.NewGuid():N}@learner.local";
        await db.Database.ExecuteSqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), {email}, 0x01, N'Studio learner', {certId});
            """);

        return await db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private static async Task<Fixture> SeedAsync(string connectionString, int sectionUnitCount = 1)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"G{Guid.NewGuid():N}"[..10];
        var cert = new Certificate { Code = code, Name = $"Group cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, DurationMinutes = 45, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P7",
            Name = "Reading Comprehension",
            OptionCount = 4,
            ClusterSize = 2,
            UnitCount = sectionUnitCount,
            ExpectedQuestionCount = 3,
            DisplayOrder = 1,
            NavigationMode = "UNIT_LOCKED_SANDBOX",
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            // Published, because a sitting may only be created against a published paper
            // (trg_ExamAttempts_PublishedExamOnly). No path here publishes anything.
            CertId = cert.CertId, Name = $"Group paper {code}", Status = "published",
            DurationMinutes = 45, Purpose = "mock", QuestionCount = 3,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // One passage for the bundle, and one position the pool deliberately leaves without a stimulus.
        var bundle = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading",
            "Bundle item 1: the depot opens _____ six.", displayOrder: 1, passage: "The depot notice.");

        var stimulusId = await db.Items.AsNoTracking()
            .Where(i => i.ItemId == bundle.ItemId)
            .Select(i => i.StimulusId!.Value)
            .SingleAsync();

        var sibling = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading",
            "Bundle item 2: it closes _____ nine.", displayOrder: 2, stimulusId: stimulusId);

        var solo = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading",
            "Solo item: the sign _____ outside.", displayOrder: 3);

        return new Fixture(
            cert.CertId, exam.ExamId, section.SectionId, stimulusId,
            [bundle.ExamItemId, sibling.ExamItemId], [solo.ExamItemId]);
    }
}
