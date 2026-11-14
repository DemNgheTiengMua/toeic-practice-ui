using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PracticeUnlockAndStatelessTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private async Task<(int UserId, int CertId, int ReadSkillId, int WriteSkillId, int ReadSecId, int WriteSecId, int ReadTopicId, int ReadItemId, int ReadVersionId)> SeedLearnerAndPathAsync(bool markReadDone = false)
    {
        var certCode = Unique("PU");
        var email = $"{Guid.NewGuid():N}@test.local";

        await database.SqlAsync($"""
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('reading','R-DET',N'Detail'),
              ('writing','W-CORR',N'Correspondence')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Practice Learner', {cert.CertId});
            """);
        var user = await db.Users.SingleAsync(u => u.Email == email);

        var readSkill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        var writeSkill = new Skill { CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 2 };
        db.Skills.AddRange(readSkill, writeSkill);
        await db.SaveChangesAsync();

        db.ScoreBands.AddRange(
            new ScoreBand { CertId = cert.CertId, Code = "A2", Name = "A2", MinTotal = 0, MaxTotal = 399, DisplayOrder = 1, CumulativeStudyHours = 0 },
            new ScoreBand { CertId = cert.CertId, Code = "B1", Name = "B1", MinTotal = 400, MaxTotal = 699, DisplayOrder = 2, CumulativeStudyHours = 20 });
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = readSkill.SkillId, Modality = "reading", Code = "R1", Name = "Reading Part 1", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        var writeSec = new Section { CertId = cert.CertId, SkillId = writeSkill.SkillId, Modality = "writing", Code = "W1", Name = "Writing Task 1", DisplayOrder = 1, ExpectedQuestionCount = 1 };
        db.Sections.AddRange(readSec, writeSec);
        await db.SaveChangesAsync();

        var rTopicId = await db.Topics.Where(t => t.Modality == "reading").Select(t => t.TopicId).FirstAsync();
        var wTopicId = await db.Topics.Where(t => t.Modality == "writing").Select(t => t.TopicId).FirstAsync();

        var readItem = new Item
        {
            Modality = "reading",
            TopicId = rTopicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            RenderFormat = "mcq",
            TargetBands = "ALL",
            LifecycleStatus = "active",
            PlacementEligible = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Items.Add(readItem);
        await db.SaveChangesAsync();

        var rv = new ItemVersion
        {
            ItemId = readItem.ItemId,
            VersionNumber = 1,
            Stem = "Pick A.",
            OptionA = "Yes",
            OptionB = "No",
            OptionC = "Maybe",
            OptionD = "Never",
            KeyLabel = "A",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ItemVersions.Add(rv);
        await db.SaveChangesAsync();

        var mockExam = new Exam { CertId = cert.CertId, Name = $"Mock {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
        db.Exams.Add(mockExam);
        await db.SaveChangesAsync();

        db.ExamItems.Add(new ExamItem
        {
            ExamId = mockExam.ExamId,
            CertId = cert.CertId,
            SectionId = readSec.SectionId,
            ItemId = readItem.ItemId,
            ItemVersionId = rv.ItemVersionId,
            Modality = "reading",
            DisplayOrder = 1
        });
        await db.SaveChangesAsync();

        var path = new LearnerPath
        {
            UserId = user.UserId,
            CertId = cert.CertId,
            CurrentBandCode = "A2",
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow,
            WindowStartDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.LearnerPaths.Add(path);
        await db.SaveChangesAsync();

        db.LearnerPathSteps.AddRange(
            new LearnerPathStep
            {
                LearnerPathId = path.LearnerPathId,
                CertId = cert.CertId,
                WeekNumber = 1,
                WeekSequence = 1,
                StepType = PathStepType.FocusPractice.ToCode(),
                Status = markReadDone ? "done" : "available",
                SkillId = readSkill.SkillId,
                Modality = "reading",
                TopicId = rTopicId,
                TargetCefr = "A2",
                SizingTier = SizingTier.Developing.ToCode(),
                PlannedItemCount = 1,
                EstimatedMinutes = 10,
                TargetAccuracyPct = 70
            },
            new LearnerPathStep
            {
                LearnerPathId = path.LearnerPathId,
                CertId = cert.CertId,
                WeekNumber = 1,
                WeekSequence = 2,
                StepType = PathStepType.FocusPractice.ToCode(),
                Status = "locked",
                SkillId = writeSkill.SkillId,
                Modality = "writing",
                TopicId = wTopicId,
                TargetCefr = "A2",
                SizingTier = SizingTier.Developing.ToCode(),
                PlannedItemCount = 1,
                EstimatedMinutes = 15,
                TargetAccuracyPct = 70
            });
        await db.SaveChangesAsync();

        return (user.UserId, cert.CertId, readSkill.SkillId, writeSkill.SkillId, readSec.SectionId, writeSec.SectionId, rTopicId, readItem.ItemId, rv.ItemVersionId);
    }

    [Fact]
    public async Task GetPracticeSectionsAsync_UnlocksStrictlyOnPathStepDone_AndReturnsNoUnfinishedSession()
    {
        var (userId, certId, _, _, readSecId, writeSecId, _, _, _) = await SeedLearnerAndPathAsync(markReadDone: false);
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Even if an in_progress session exists, stateless Practice must not surface UnfinishedSession.
        db.PracticeSessions.Add(new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = readSecId,
            Modality = "reading",
            QuestionCount = 1,
            Status = "in_progress",
            CorrectCount = 0,
            QuestionSource = "all",
            StartedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new PracticeService(db, new LearnerService(db));
        var lockedCatalog = await service.GetPracticeSectionsAsync(userId);

        Assert.Null(lockedCatalog.UnfinishedSession);
        Assert.False(lockedCatalog.ReadingSections.Single(s => s.SectionId == readSecId).IsUnlocked);
        Assert.False(lockedCatalog.OtherSections.Single(s => s.SectionId == writeSecId).IsUnlocked);

        // Complete the Reading step in the active Learning Path -> Reading unlocks, Writing stays locked.
        var readStep = await db.LearnerPathSteps.FirstAsync(s => s.LearnerPath.UserId == userId && s.Modality == "reading");
        readStep.Status = "done";
        readStep.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var unlockedCatalog = await service.GetPracticeSectionsAsync(userId);
        Assert.Null(unlockedCatalog.UnfinishedSession);
        Assert.True(unlockedCatalog.ReadingSections.Single(s => s.SectionId == readSecId).IsUnlocked);
        Assert.False(unlockedCatalog.OtherSections.Single(s => s.SectionId == writeSecId).IsUnlocked);
    }

    [Fact]
    public async Task StartSessionAsync_RejectsLockedSectionWithFreeDrillLocked_AndAllowsUnlockedSection()
    {
        var (userId, _, _, _, readSecId, _, _, _, _) = await SeedLearnerAndPathAsync(markReadDone: false);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PracticeService(db, new LearnerService(db));

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            service.StartSessionAsync(userId, new StartPracticeSessionInput(readSecId)));
        Assert.Equal(PracticeException.FreeDrillLocked, ex.Code);

        var readStep = await db.LearnerPathSteps.FirstAsync(s => s.LearnerPath.UserId == userId && s.Modality == "reading");
        readStep.Status = "done";
        readStep.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var session = await service.StartSessionAsync(userId, new StartPracticeSessionInput(readSecId));
        Assert.True(session.SessionId > 0);
        Assert.Equal(1, session.QuestionCount);
    }

    [Fact]
    public async Task PracticeSession_ScoresAnswerAndUpdatesSectionAccuracyPct()
    {
        var (userId, _, _, _, readSecId, _, _, _, versionId) = await SeedLearnerAndPathAsync(markReadDone: true);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PracticeService(db, new LearnerService(db));

        var session = await service.StartSessionAsync(userId, new StartPracticeSessionInput(readSecId));
        var take = await service.GetSessionTakeAsync(userId, session.SessionId);
        var q = Assert.Single(take.Questions);
        var optA = q.Options.Single(o => o.Label == "A");

        var graded = await service.SubmitAnswerAsync(
            userId, session.SessionId, q.ItemVersionId ?? versionId,
            new SubmitPracticeAnswerInput(optA.OptionId, null));

        Assert.True(graded.IsCorrect);
        var finished = await service.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.Finished, finished.Status);

        var catalog = await service.GetPracticeSectionsAsync(userId);
        var sec = catalog.ReadingSections.Single(s => s.SectionId == readSecId);
        Assert.True(sec.IsUnlocked);
        Assert.Equal(100, sec.AccuracyPercentage);
    }
}
