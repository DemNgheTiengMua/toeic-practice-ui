using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #235 / D11: <b>one</b> selector, proven at the call sites rather than only at the selector.
/// <para>
/// The brief's trap is that a test exercising the new selector passes while the old draw stays in place,
/// so this class does two things: it greps <c>src/</c> for the deleted draws, and it drives each origin
/// through its real public service and asserts the two agree on the same coordinate.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OneSelectorCallSiteTests(DatabaseFixture database)
{
    /// <summary>
    /// The deleted draws, by the shape that names them. A path draw that re-derives its own ordering
    /// carries <c>LastWrongAt</c> and a literal <c>MasterySchedule.WrongAnswerCooldown</c> comparison;
    /// a drill draw that re-derives its own ordering carries its own <c>GateContext.NewestPerItem</c>
    /// call. Both now live only in <see cref="ItemSelector"/>.
    /// <para>
    /// The input that would pass a weaker test while being wrong is any assertion on behaviour alone: an
    /// old draw left in place still produces plausible items. This asserts the code is <b>absent</b>.
    /// </para>
    /// </summary>
    [Fact]
    public void The_two_former_draws_no_longer_exist_outside_the_one_selector()
    {
        var servicesRoot = Path.Combine(DatabaseFixture.RepoRootPath, "src", "Services");

        var withOldPathDraw = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("LastWrongAt + MasterySchedule.WrongAnswerCooldown", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(withOldPathDraw);

        var withOwnNewestVersionQuery = Directory
            .EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("GateContext.NewestPerItem", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // One selector, and the authoring/audit readers that ask "which version is live" about a bundle
        // rather than about a cell. Neither former draw is among them: a practice-path reader that
        // reappeared here would be a second newest-version rule for selection.
        Assert.DoesNotContain("LearningPathService.Steps.cs", withOwnNewestVersionQuery);
        Assert.DoesNotContain("PracticeService.Pool.cs", withOwnNewestVersionQuery);
        Assert.Contains("ItemSelector.cs", withOwnNewestVersionQuery);
    }

    /// <summary>
    /// A path step and a free drill, both through their real public services, draw the same item at the
    /// same coordinate. Passing while wrong: each service reaching the pool its own way, which agrees
    /// only until one of them learns a new exclusion.
    /// </summary>
    [Fact]
    public async Task A_path_step_and_a_free_drill_from_the_services_draw_the_same_item()
    {
        var name = $"EEOne_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
                  INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
                INSERT INTO Certificates (Code, Name, IsActive) VALUES ('ONE235', N'One selector', 1);
                DECLARE @cert INT = SCOPE_IDENTITY();
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
                VALUES (@cert, 'READ', N'Reading', 'reading', 1);
                DECLARE @skill INT = SCOPE_IDENTITY();
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail');
                DECLARE @topic INT = SCOPE_IDENTITY();

                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', @topic, 'B1', 'sentence_cloze', 0, 'production', 'active', 'one-seen');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'Seen', N'a', N'b', N'c', N'd', 'A');

                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', @topic, 'B1', 'sentence_cloze', 0, 'production', 'active', 'one-unseen');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'Unseen', N'a', N'b', N'c', N'd', 'A');

                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'one-drill@test.local', 0x01, N'D', @cert);
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'one-path@test.local', 0x01, N'P', @cert);

                -- the path's step, over the same cell, open and ready
                INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
                VALUES ((SELECT UserId FROM Users WHERE Email = 'one-path@test.local'), @cert, 'A2', 'active');
                DECLARE @path INT = SCOPE_IDENTITY();
                INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                    StepType, SizingTier, Status, WeekNumber, WeekSequence, PlannedItemCount)
                VALUES (@path, @cert, @skill, 'reading', @topic, 'B1', 'focus_practice', 'developing',
                    'available', 1, 1, 10);
                """);

            var certId = await db.Certificates.AsNoTracking().Where(c => c.Code == "ONE235").Select(c => c.CertId).SingleAsync();
            var topicId = await db.Topics.AsNoTracking().Where(t => t.Modality == "reading" && t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();
            var drillUser = await db.Users.AsNoTracking().Where(u => u.Email == "one-drill@test.local").Select(u => u.UserId).SingleAsync();
            var pathUser = await db.Users.AsNoTracking().Where(u => u.Email == "one-path@test.local").Select(u => u.UserId).SingleAsync();
            var stepId = await db.LearnerPathSteps.AsNoTracking().Select(s => s.StepId).SingleAsync();

            var practice = new PracticeService(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));
            var paths = new LearningPathService(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));

            var drillSession = await practice.StartSessionAsync(drillUser, new StartPracticeSessionInput(topicId, CefrLevel.B1));
            var stepResult = await paths.StartStepAsync(pathUser, stepId);

            var drillItems = await db.PracticeAnswers.AsNoTracking()
                .Where(a => a.SessionId == drillSession.SessionId)
                .OrderBy(a => a.DisplayOrder)
                .Select(a => a.ItemId!.Value)
                .ToListAsync();

            var pathItems = await db.PracticeAnswers.AsNoTracking()
                .Where(a => a.SessionId == stepResult.SessionId!.Value)
                .OrderBy(a => a.DisplayOrder)
                .Select(a => a.ItemId!.Value)
                .ToListAsync();

            // Both items are unseen for both learners, so the tie-break is the identifier — the third key
            // of PLC-58's ordering, and the reason a repeated draw is reproducible.
            var seen = await db.Items.AsNoTracking().Where(i => i.BatchTag == "one-seen").Select(i => i.ItemId).SingleAsync();
            var unseen = await db.Items.AsNoTracking().Where(i => i.BatchTag == "one-unseen").Select(i => i.ItemId).SingleAsync();
            var expectedFirst = Math.Min(seen, unseen);

            Assert.Equal([expectedFirst, Math.Max(seen, unseen)], drillItems);
            Assert.Equal(drillItems, pathItems);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// PLC-60 asserted through the service the learner actually clicks: the step is served from a sibling
    /// topic, the outcome names it, and the session is still linked to the step. Passing while wrong: a
    /// step that refuses because its own cell is cooling.
    /// </summary>
    [Fact]
    public async Task A_path_step_whose_cell_is_cooling_starts_from_a_sibling_topic_and_reports_it()
    {
        var name = $"EEOne_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
                  INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
                INSERT INTO Certificates (Code, Name, IsActive) VALUES ('SIB235', N'Sibling', 1);
                DECLARE @cert INT = SCOPE_IDENTITY();
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
                VALUES (@cert, 'READ', N'Reading', 'reading', 1);
                DECLARE @skill INT = SCOPE_IDENTITY();
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail');
                DECLARE @own INT = SCOPE_IDENTITY();
                INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-INF', N'Inference');
                DECLARE @sibling INT = SCOPE_IDENTITY();

                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', @own, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sib-own');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'Own', N'a', N'b', N'c', N'd', 'A');
                INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus, BatchTag)
                VALUES ('reading', @sibling, 'B1', 'sentence_cloze', 0, 'production', 'active', 'sib-other');
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (SCOPE_IDENTITY(), 1, N'Other', N'a', N'b', N'c', N'd', 'A');

                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'sib235@test.local', 0x01, N'P', @cert);
                DECLARE @user INT = SCOPE_IDENTITY();
                INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
                VALUES (@user, @cert, 'A2', 'active');
                DECLARE @path INT = SCOPE_IDENTITY();
                INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr,
                    StepType, SizingTier, Status, WeekNumber, WeekSequence, PlannedItemCount)
                VALUES (@path, @cert, @skill, 'reading', @own, 'B1', 'focus_practice', 'developing',
                    'available', 1, 1, 5);
                """);

            var userId = await db.Users.AsNoTracking().Where(u => u.Email == "sib235@test.local").Select(u => u.UserId).SingleAsync();
            var stepId = await db.LearnerPathSteps.AsNoTracking().Select(s => s.StepId).SingleAsync();
            var ownItem = await db.Items.AsNoTracking().Where(i => i.BatchTag == "sib-own").Select(i => i.ItemId).SingleAsync();
            var siblingItem = await db.Items.AsNoTracking().Where(i => i.BatchTag == "sib-other").Select(i => i.ItemId).SingleAsync();
            var versionId = await db.ItemVersions.AsNoTracking().Where(v => v.ItemId == ownItem).Select(v => v.ItemVersionId).SingleAsync();
            var topicId = await db.Topics.AsNoTracking().Where(t => t.Modality == "reading" && t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();
            var certId = await db.Certificates.AsNoTracking().Where(c => c.Code == "SIB235").Select(c => c.CertId).SingleAsync();

            // the step's own item was answered wrongly an hour ago, so its cell is cooling
            var priorSession = new BusinessObjects.Entities.PracticeSession
            {
                UserId = userId,
                CertId = certId,
                TopicId = topicId,
                CefrLevel = CefrLevel.B1,
                Modality = "reading",
                QuestionCount = 1,
                Status = "finished",
                QuestionSource = "all",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-2),
            };
            db.PracticeSessions.Add(priorSession);
            await db.SaveChangesAsync();
            db.PracticeAnswers.Add(new BusinessObjects.Entities.PracticeAnswer
            {
                SessionId = priorSession.SessionId,
                ItemId = ownItem,
                ItemVersionId = versionId,
                SelectedLabel = "A",
                IsCorrect = false,
                AnsweredAt = DateTime.UtcNow.AddHours(-1),
                DisplayOrder = 1,
            });
            await db.SaveChangesAsync();

            var paths = new LearningPathService(DbContextFactory.Create(connectionString), new LearnerService(DbContextFactory.Create(connectionString)));
            var result = await paths.StartStepAsync(userId, stepId);

            Assert.NotNull(result.SessionId);
            Assert.Equal(topicId, result.SubstitutedFromTopicId);

            var drawn = await db.PracticeAnswers.AsNoTracking()
                .Where(a => a.SessionId == result.SessionId!.Value)
                .Select(a => a.ItemId!.Value)
                .ToListAsync();

            Assert.Equal([siblingItem], drawn);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
