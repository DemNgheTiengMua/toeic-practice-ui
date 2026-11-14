using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LearningPath5CreditTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (LearningPathService service, CreditLedger ledger, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var ledger = new CreditLedger(db);
        var service = new LearningPathService(db, learners, ledger);
        return (service, ledger, db);
    }

    private async Task<int> SeedLearnerWithCreditsAsync(string certCode, int credits)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Path Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (credits > 0)
        {
            var adminId = await db.Users.Where(u => u.UserId != userId).Select(u => (int?)u.UserId).FirstOrDefaultAsync() ?? userId;
            db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId,
                Reason = CreditReason.AdminGrant.ToCode(),
                Delta = credits,
                AdminUserId = adminId,
                Note = "Test seed credits",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        return userId;
    }

    private async Task<(int CertId, int ReadSkillId, int WriteSkillId, int ExamId)> SeedCertWithAllAndBandContentAsync(
        string certCode, string itemTargetBands = "ALL", string lessonTargetBands = "ALL")
    {
        await database.SqlAsync("""
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

        var readSkill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        var writeSkill = new Skill { CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 2 };
        db.Skills.AddRange(readSkill, writeSkill);
        await db.SaveChangesAsync();

        db.ScoreBands.AddRange(
            new ScoreBand { CertId = cert.CertId, Code = "A2", Name = "A2", MinTotal = 0, MaxTotal = 399, DisplayOrder = 1, CumulativeStudyHours = 0 },
            new ScoreBand { CertId = cert.CertId, Code = "B1", Name = "B1", MinTotal = 400, MaxTotal = 699, DisplayOrder = 2, CumulativeStudyHours = 20 },
            new ScoreBand { CertId = cert.CertId, Code = "B2", Name = "B2", MinTotal = 700, MaxTotal = 990, DisplayOrder = 3, CumulativeStudyHours = 40 });
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = readSkill.SkillId, Modality = "reading", Code = "R1", Name = "Reading Part 1", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        var writeSec = new Section { CertId = cert.CertId, SkillId = writeSkill.SkillId, Modality = "writing", Code = "W1", Name = "Writing Task 1", DisplayOrder = 1, ExpectedQuestionCount = 1 };
        db.Sections.AddRange(readSec, writeSec);
        await db.SaveChangesAsync();

        var rTopicId = await db.Topics.Where(t => t.Modality == "reading").Select(t => t.TopicId).FirstAsync();
        var wTopicId = await db.Topics.Where(t => t.Modality == "writing").Select(t => t.TopicId).FirstAsync();

        var readLesson = new Lesson
        {
            CertId = cert.CertId,
            SkillId = readSkill.SkillId,
            Modality = "reading",
            TopicId = rTopicId,
            CefrLevel = CefrLevel.B1,
            TargetBands = lessonTargetBands,
            Title = "Reading Foundations",
            Summary = "Reading theory",
            EstimatedMinutes = 10,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var writeLesson = new Lesson
        {
            CertId = cert.CertId,
            SkillId = writeSkill.SkillId,
            Modality = "writing",
            TopicId = wTopicId,
            CefrLevel = CefrLevel.B1,
            TargetBands = lessonTargetBands,
            Title = "Writing Guide",
            Summary = "Writing rubric & structure",
            EstimatedMinutes = 15,
            DisplayOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Lessons.AddRange(readLesson, writeLesson);

        var readItem = new Item
        {
            Modality = "reading",
            TopicId = rTopicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            RenderFormat = "mcq",
            TargetBands = itemTargetBands,
            LifecycleStatus = "active",
            PlacementEligible = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var writeItem = new Item
        {
            Modality = "writing",
            TopicId = wTopicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "transactional_correspondence",
            RenderFormat = "free_response",
            TargetBands = itemTargetBands,
            LifecycleStatus = "active",
            PlacementEligible = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Items.AddRange(readItem, writeItem);
        await db.SaveChangesAsync();

        var rv = new ItemVersion
        {
            ItemId = readItem.ItemId,
            VersionNumber = 1,
            Stem = "Choose the best answer.",
            OptionA = "Right",
            OptionB = "Wrong",
            OptionC = "Other",
            OptionD = "None",
            KeyLabel = "A",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var wv = new ItemVersion
        {
            ItemId = writeItem.ItemId,
            VersionNumber = 1,
            Stem = "Write a short email.",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ItemVersions.AddRange(rv, wv);
        await db.SaveChangesAsync();

        var mockExam = new Exam { CertId = cert.CertId, Name = $"Mock {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
        db.Exams.Add(mockExam);
        await db.SaveChangesAsync();

        db.ExamItems.AddRange(
            new ExamItem { ExamId = mockExam.ExamId, CertId = cert.CertId, SectionId = readSec.SectionId, ItemId = readItem.ItemId, ItemVersionId = rv.ItemVersionId, Modality = "reading", DisplayOrder = 1 },
            new ExamItem { ExamId = mockExam.ExamId, CertId = cert.CertId, SectionId = writeSec.SectionId, ItemId = writeItem.ItemId, ItemVersionId = wv.ItemVersionId, Modality = "writing", DisplayOrder = 1 });
        await db.SaveChangesAsync();

        return (cert.CertId, readSkill.SkillId, writeSkill.SkillId, mockExam.ExamId);
    }

    private async Task SeedGoalAndPlacementAsync(int userId, int certId, string currentBand, string targetBand)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var placementExam = new Exam { CertId = certId, Name = $"Placement {Guid.NewGuid():N}"[..20], Status = "published", DurationMinutes = 30, Purpose = "placement" };
        db.Exams.Add(placementExam);
        await db.SaveChangesAsync();

        var attempt = new ExamAttempt
        {
            UserId = userId,
            ExamId = placementExam.ExamId,
            CertId = certId,
            Status = "graded",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TotalScore = 300,
            BandCode = currentBand
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var skillId = await db.Skills.Where(s => s.CertId == certId).Select(s => s.SkillId).FirstAsync();
        db.AttemptSkillScores.Add(new AttemptSkillScore
        {
            AttemptId = attempt.AttemptId,
            SkillId = skillId,
            CertId = certId,
            RawScore = 10,
            ScaledScore = 300
        });
        await db.SaveChangesAsync();

        var pt = new PlacementTest
        {
            UserId = userId,
            CertId = certId,
            AttemptId = attempt.AttemptId,
            Scope = "all_skills",
            Status = "done",
            ResultBandCode = currentBand,
            ReadingPct = 45m,
            ListeningPct = 45m,
            SpeakingPct = 45m,
            WritingPct = 45m,
            TakenAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        db.PlacementTests.Add(pt);
        await db.SaveChangesAsync();
        db.TargetBands.Add(new TargetBand
        {
            UserId = userId,
            CertId = certId,
            CurrentBandCode = currentBand,
            TargetBandCode = targetBand,
            WeeklyStudyHours = 5,
            TargetDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60),
            FeasibilityStatus = FeasibilityStatus.Feasible.ToCode(),
            RequiredTotalHours = 20,
            SetAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GeneratePathAsync_BlocksBeforeCharge_WhenWalletBalanceBelowFiveCredits()
    {
        var certCode = Unique("P5");
        var (certId, _, _, _) = await SeedCertWithAllAndBandContentAsync(certCode);
        var userId = await SeedLearnerWithCreditsAsync(certCode, credits: 4);
        await SeedGoalAndPlacementAsync(userId, certId, "A2", "B2");

        var (service, ledger, db) = CreateServices();

        var ex = await Assert.ThrowsAsync<CreditLedgerException>(() => service.GeneratePathAsync(userId));
        Assert.Equal(CreditLedgerException.InsufficientCredits, ex.Code);
        Assert.Equal(402, ex.StatusCode);

        Assert.Equal(4, await ledger.GetBalanceAsync(userId));
        Assert.False(await db.LearnerPaths.AnyAsync(p => p.UserId == userId && p.CertId == certId));
    }

    [Fact]
    public async Task GeneratePathAsync_DeductsFiveCredits_WithLearningPathReason_AndBuilds4WeekSequentialPlan()
    {
        var certCode = Unique("P5");
        var (certId, _, _, _) = await SeedCertWithAllAndBandContentAsync(certCode);
        var userId = await SeedLearnerWithCreditsAsync(certCode, credits: 10);
        await SeedGoalAndPlacementAsync(userId, certId, "A2", "B2");

        var (service, ledger, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        Assert.Equal(5, await ledger.GetBalanceAsync(userId));

        var txn = await db.CreditTransactions.SingleAsync(t => t.UserId == userId && t.Reason == "learning_path");
        Assert.Equal(-5, txn.Delta);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .ToListAsync();

        Assert.NotEmpty(steps);
        Assert.Equal([1, 2, 3, 4], steps.Select(s => s.WeekNumber!.Value).Distinct().OrderBy(w => w).ToArray());
        Assert.Equal("available", steps[0].Status);
        Assert.All(steps.Skip(1), s => Assert.Equal("locked", s.Status));
        Assert.Contains(steps, s => s.WeekNumber == 2 && s.StepType == PathStepType.MockSection.ToCode());
        Assert.Contains(steps, s => s.WeekNumber == 4 && s.StepType == PathStepType.MockSection.ToCode());
    }

    [Fact]
    public async Task GeneratePathAsync_FallsBackToAllAndNearestBand_NeverThrowsNoQuestionsAvailable()
    {
        var certCode = Unique("FB");
        // Items & lessons are tagged "B2" only (no "A2" items at all) -> nearest-band fallback must still build a 4-week plan
        var (certId, _, _, _) = await SeedCertWithAllAndBandContentAsync(certCode, itemTargetBands: "B2", lessonTargetBands: "B2");
        var userId = await SeedLearnerWithCreditsAsync(certCode, credits: 5);
        await SeedGoalAndPlacementAsync(userId, certId, "A2", "B2");

        var (service, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        var weeks = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .Select(s => s.WeekNumber)
            .Distinct()
            .ToListAsync();
        Assert.Equal(4, weeks.Count);
    }

    [Fact]
    public async Task CloseStepAsync_UnlocksNextSequentialStep()
    {
        var certCode = Unique("SQ");
        var (certId, _, _, _) = await SeedCertWithAllAndBandContentAsync(certCode);
        var userId = await SeedLearnerWithCreditsAsync(certCode, credits: 5);
        await SeedGoalAndPlacementAsync(userId, certId, "A2", "B2");

        var (service, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        var ordered = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync();

        Assert.True(ordered.Count >= 2);
        Assert.Equal("available", ordered[0].Status);
        Assert.Equal("locked", ordered[1].Status);

        var closed = await PathStepCompletion.CloseStepAsync(
            db, ordered[0], score: null, accuracyPct: null);
        Assert.Equal(ordered[0].StepId, closed);

        await db.Entry(ordered[0]).ReloadAsync();
        await db.Entry(ordered[1]).ReloadAsync();
        Assert.Equal("done", ordered[0].Status);
        Assert.Equal("available", ordered[1].Status);
    }

    [Fact]
    public async Task WritingStep_InLearningPath_IncludesGuideAndRubric_AndChargesZeroCreditsOnAiGrading()
    {
        var certCode = Unique("WS");
        var (certId, _, _, _) = await SeedCertWithAllAndBandContentAsync(certCode);
        var userId = await SeedLearnerWithCreditsAsync(certCode, credits: 5);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var writingSkill = await db.Skills.FirstAsync(s => s.CertId == certId && s.Code == "WRIT");
        var writingSection = await db.Sections.FirstAsync(s => s.CertId == certId && s.SkillId == writingSkill.SkillId);
        var writingItem = await db.Items.FirstAsync(i => i.Modality == "writing");
        var writingTopic = await db.Topics.FirstAsync(t => t.TopicId == writingItem.TopicId);
        var writingVersion = await db.ItemVersions.FirstAsync(v => v.ItemId == writingItem.ItemId);

        db.GradingCriteria.Add(new GradingCriterion
        {
            CertId = certId,
            SkillId = writingSkill.SkillId,
            Code = $"TA_{certCode}"[..16],
            Name = "Task Achievement",
            MaxScore = 9,
            Weight = 1m,
            RubricDescription = "Addresses all parts of the prompt clearly.",
            DisplayOrder = 1
        });

        var writingExam = new Exam
        {
            CertId = certId,
            Name = $"Writing Practice {certCode}",
            Purpose = ExamPurpose.WritingPractice.ToCode(),
            Status = ExamStatus.Published.ToCode(),
            DurationMinutes = 30,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Exams.Add(writingExam);
        await db.SaveChangesAsync();

        db.ExamItems.Add(new ExamItem
        {
            ExamId = writingExam.ExamId,
            CertId = certId,
            SectionId = writingSection.SectionId,
            ItemId = writingItem.ItemId,
            ItemVersionId = writingVersion.ItemVersionId,
            Modality = "writing",
            DisplayOrder = 1
        });
        await db.SaveChangesAsync();

        await SeedGoalAndPlacementAsync(userId, certId, "A2", "B2");
        var (lpService, ledger, _) = CreateServices();
        var summary = await lpService.GeneratePathAsync(userId);
        // Wallet now has 0 credits (5 - 5 = 0).
        Assert.Equal(0, await ledger.GetBalanceAsync(userId));

        // Add an available Writing step to the active path
        var writingStep = new LearnerPathStep
        {
            LearnerPathId = summary.LearnerPathId!.Value,
            CertId = certId,
            WeekNumber = 1,
            WeekSequence = 99,
            StepType = PathStepType.FocusPractice.ToCode(),
            Status = LearnerPathStepStatus.Available.ToCode(),
            SkillId = writingSkill.SkillId,
            Modality = "writing",
            TopicId = writingTopic.TopicId,
            TargetCefr = "A2",
            SizingTier = SizingTier.Developing.ToCode(),
            PlannedItemCount = 1,
            EstimatedMinutes = 20,
            TargetAccuracyPct = 70
        };
        db.LearnerPathSteps.Add(writingStep);
        await db.SaveChangesAsync();

        var overview = await lpService.GetActivePathAsync(userId);
        var dtoStep = overview.Weeks.SelectMany(w => w.Steps).Single(s => s.StepId == writingStep.StepId);
        Assert.True(dtoStep.IsZeroCreditAiGrading);
        Assert.False(string.IsNullOrWhiteSpace(dtoStep.GuideText));
        Assert.NotEmpty(dtoStep.RubricCriteria!);

        // Start & submit WritingPractice linked to writingStep.StepId with 0 credits in wallet
        var writingService = new WritingPracticeService(db, new LearnerService(db), ledger);
        var attempt = await writingService.StartAttemptAsync(userId, writingExam.ExamId, learnerPathStepId: writingStep.StepId);
        var submit = await writingService.SubmitAttemptAsync(userId, attempt.AttemptId, learnerPathStepId: writingStep.StepId);

        Assert.Equal(AttemptStatus.Submitted, submit.Status);
        Assert.Equal(0, await ledger.GetBalanceAsync(userId));
        Assert.True(await db.AiGradings.AnyAsync(g => g.FreeResponse.AttemptId == attempt.AttemptId));

        await db.Entry(writingStep).ReloadAsync();
        Assert.Equal("done", writingStep.Status);
    }
}
