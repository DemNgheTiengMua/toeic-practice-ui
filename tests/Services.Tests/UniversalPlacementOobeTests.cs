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

[Collection(DatabaseCollection.Name)]
public sealed class UniversalPlacementOobeTests(DatabaseFixture database)
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private Task SqlAsync(string batch) => database.SqlAsync(batch);

    private sealed record UniversalFixture(
        string ToeicCode,
        string IeltsCode,
        int ToeicCertId,
        int IeltsCertId,
        int UniversalExamId,
        IReadOnlyDictionary<string, int> SkillIds);

    private async Task<UniversalFixture> SeedUniversalCatalogAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var toeicCode = $"T_{tag}";
        var ieltsCode = $"I_{tag}";
        var examName = $"Universal Placement Exam {tag}";

        await SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule) VALUES
              ('{toeicCode}', N'TOEIC Listening & Reading', 1, 'sum'),
              ('{ieltsCode}', N'IELTS Academic', 1, 'avg_half_step');

            DECLARE @toeic INT = (SELECT CertId FROM Certificates WHERE Code = '{toeicCode}');
            DECLARE @ielts INT = (SELECT CertId FROM Certificates WHERE Code = '{ieltsCode}');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@toeic, 'A2', N'Elementary (255-400)', 10, 400, 1, 100),
              (@toeic, 'B1', N'Intermediate (405-600)', 405, 600, 2, 220),
              (@toeic, 'B2', N'Upper-Intermediate (605-780)', 605, 780, 3, 360),
              (@toeic, 'C1', N'Advanced (785-990)', 785, 990, 4, 520),
              (@ielts, '4.5', N'Band 4.5', 0, 45, 1, 120),
              (@ielts, '5.5', N'Band 5.5', 46, 55, 2, 240),
              (@ielts, '6.5', N'Band 6.5', 56, 65, 3, 400),
              (@ielts, '7.5', N'Band 7.5', 66, 90, 4, 600);

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@toeic, 'LIST', N'Listening', 'listening', 1, 5, 495, 5),
              (@toeic, 'READ', N'Reading', 'reading', 2, 5, 495, 5),
              (@ielts, 'LIST', N'Listening', 'listening', 1, 0, 90, 5),
              (@ielts, 'READ', N'Reading', 'reading', 2, 0, 90, 5),
              (@ielts, 'SPEAK', N'Speaking', 'speaking', 3, 0, 90, 5),
              (@ielts, 'WRIT', N'Writing', 'writing', 4, 0, 90, 5);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder, RequireAudio) VALUES
              ((SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'LIST'), @ielts, 'listening', 'U-L', N'Universal Listening', 2, 1, 0),
              ((SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'READ'), @ielts, 'reading', 'U-R', N'Universal Reading', 2, 2, 0),
              ((SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'SPEAK'), @ielts, 'speaking', 'U-S', N'Universal Speaking', 1, 3, 0),
              ((SELECT SkillId FROM Skills WHERE CertId = @ielts AND Code = 'WRIT'), @ielts, 'writing', 'U-W', N'Universal Writing', 1, 4, 0);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES (@ielts, N'{examName}', 'published', 60, 'placement', 6);

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @ielts, SkillId, NULL, 0, 0 FROM Skills WHERE CertId = @ielts;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @ielts, SkillId, NULL, 1, 45 FROM Skills WHERE CertId = @ielts;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @ielts, SkillId, NULL, 2, 70 FROM Skills WHERE CertId = @ielts;
            """);

        await using var db = Db();
        var toeicId = await db.Certificates.Where(c => c.Code == toeicCode).Select(c => c.CertId).SingleAsync();
        var ieltsId = await db.Certificates.Where(c => c.Code == ieltsCode).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.Name == examName).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == ieltsId).ToDictionaryAsync(s => s.Modality, s => s.SkillId);
        var sections = await db.Sections.Where(s => s.CertId == ieltsId).ToDictionaryAsync(s => s.Modality, s => s.SectionId);

        await PoolFixture.AddMcqAsync(db, examId, ieltsId, sections["listening"], "listening", "L1", displayOrder: 1);
        await PoolFixture.AddMcqAsync(db, examId, ieltsId, sections["listening"], "listening", "L2", displayOrder: 2);

        await PoolFixture.AddMcqAsync(db, examId, ieltsId, sections["reading"], "reading", "R1", displayOrder: 1);
        await PoolFixture.AddMcqAsync(db, examId, ieltsId, sections["reading"], "reading", "R2", displayOrder: 2);

        await PoolFixture.AddProductiveAsync(db, examId, ieltsId, sections["speaking"], "speaking", "S1", displayOrder: 1);
        await PoolFixture.AddProductiveAsync(db, examId, ieltsId, sections["writing"], "writing", "W1", displayOrder: 1);

        return new UniversalFixture(toeicCode, ieltsCode, toeicId, ieltsId, examId, skillIds);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null, int credits = 0)
    {
        var email = $"u-{Guid.NewGuid():N}@test.local";
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = Db();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (credits > 0)
        {
            await SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@test.local')
                  INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                  VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                DECLARE @adminId INT = (SELECT UserId FROM Users WHERE Email = 'admin@test.local');
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {credits}, @adminId, N'Test setup');
                """);
        }

        return userId;
    }

    [Fact]
    public async Task Universal_placement_catalog_and_start_work_across_certificates_with_scope_filtering()
    {
        var fx = await SeedUniversalCatalogAsync();
        // Learner's active cert is TOEIC, while the Universal Placement Exam is hosted on IELTS (CertId=2)
        var userId = await SeedLearnerAsync(fx.ToeicCode);

        await using var db = Db();
        var learners = new LearnerService(db);
        var placement = new PlacementService(db, learners);

        var catalog = await placement.GetPlacementCatalogAsync(userId);
        Assert.False(catalog.IsRetake);
        Assert.Contains(catalog.Exams, e => e.ExamId == fx.UniversalExamId);

        var started = await placement.StartPlacementTestAsync(
            userId,
            fx.UniversalExamId,
            PlacementReuseChoice.Remeasure,
            PlacementScope.ListeningAndReading);

        Assert.Equal(0, started.CreditsCharged);
        Assert.Equal(PlacementScope.ListeningAndReading, started.Scope);

        await new AttemptEngine(db).VerifyPreflightAsync(started.AttemptId, userId);
        var take = await placement.GetPlacementTakeAsync(userId, started.PlacementId, null);

        Assert.Equal(4, take.Questions.Count);
        Assert.All(take.Questions, q =>
            Assert.True(q.SkillCode is "LIST" or "READ", $"Unexpected skill {q.SkillCode} in 2-skill placement"));
    }

    [Fact]
    public async Task Submitting_2_skill_universal_placement_persists_percentages_and_maps_all_active_certificates()
    {
        var fx = await SeedUniversalCatalogAsync();
        var userId = await SeedLearnerAsync(fx.ToeicCode);

        await using var db = Db();
        var learners = new LearnerService(db);
        var placement = new PlacementService(db, learners);

        var started = await placement.StartPlacementTestAsync(
            userId,
            fx.UniversalExamId,
            PlacementReuseChoice.Remeasure,
            PlacementScope.ListeningAndReading);

        await new AttemptEngine(db).VerifyPreflightAsync(started.AttemptId, userId);
        var take = await placement.GetPlacementTakeAsync(userId, started.PlacementId, null);

        var listeningQs = take.Questions.Where(q => q.SkillCode == "LIST").ToList();
        var readingQs = take.Questions.Where(q => q.SkillCode == "READ").ToList();

        // Answer both Listening correctly (Option A = correct in PoolFixture)
        foreach (var q in listeningQs)
        {
            await placement.GetPlacementTakeAsync(userId, started.PlacementId, q.DisplayOrder);
            var correctOpt = q.Options.Single(o => o.Label == "A").OptionId;
            await placement.SaveAnswerAsync(userId, started.PlacementId, q.QuestionId, correctOpt, isFlagged: false);
        }

        // Answer 1 Reading correctly, 1 Reading incorrectly (50%)
        await placement.GetPlacementTakeAsync(userId, started.PlacementId, readingQs[0].DisplayOrder);
        var rCorrect = readingQs[0].Options.Single(o => o.Label == "A").OptionId;
        await placement.SaveAnswerAsync(userId, started.PlacementId, readingQs[0].QuestionId, rCorrect, isFlagged: false);

        await placement.GetPlacementTakeAsync(userId, started.PlacementId, readingQs[1].DisplayOrder);
        var rWrong = readingQs[1].Options.Single(o => o.Label == "B").OptionId;
        await placement.SaveAnswerAsync(userId, started.PlacementId, readingQs[1].QuestionId, rWrong, isFlagged: false);

        var result = await placement.SubmitPlacementTestAsync(userId, started.PlacementId, isAutoSubmit: false);

        Assert.Equal(100m, result.ListeningPct);
        Assert.Equal(50m, result.ReadingPct);
        Assert.Null(result.SpeakingPct);
        Assert.Null(result.WritingPct);
        Assert.Equal(75m, result.OverallPct);

        Assert.NotNull(result.CertificatePreviews);
        var toeicPreview = Assert.Single(result.CertificatePreviews, p => p.CertId == fx.ToeicCertId);
        Assert.Equal(2, toeicPreview.SkillCount);
        Assert.Equal("B2", toeicPreview.MappedBandCode); // 10 + 0.75*(990-10) = 745 -> B2 (605..780)

        var ieltsPreview = Assert.Single(result.CertificatePreviews, p => p.CertId == fx.IeltsCertId);
        Assert.Equal(4, ieltsPreview.SkillCount);
        Assert.Equal("7.5", ieltsPreview.MappedBandCode); // 0 + 0.75*(90-0) = 67.5 -> 68 -> 7.5 (66..90)

        // OOBE Step 3: SetGoalAsync on TOEIC resolves CurrentBandCode = "B2" from universal placement
        var goalResult = await placement.SetGoalAsync(
            userId,
            currentBandCode: null,
            targetBandCode: "C1",
            targetDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)),
            weeklyStudyHours: 10m);

        Assert.Equal("B2", goalResult.Feasibility!.CurrentBandCode);
        Assert.Equal("C1", goalResult.Feasibility.TargetBandCode);
        Assert.NotNull(goalResult.Feasibility.Status);
    }

    [Fact]
    public async Task OnboardingState_tracks_placement_goal_and_learning_path_until_oobe_complete()
    {
        var fx = await SeedUniversalCatalogAsync();
        var userId = await SeedLearnerAsync(fx.ToeicCode);

        await using var db = Db();
        var learners = new LearnerService(db);
        var placement = new PlacementService(db, learners);

        // 1. Fresh learner: nothing complete
        var s0 = await learners.GetOnboardingStateAsync(userId);
        Assert.False(s0.HasCompletedPlacement);
        Assert.Null(s0.ActivePlacementAttemptId);
        Assert.False(s0.HasTargetGoal);
        Assert.False(s0.HasActiveLearningPath);
        Assert.False(s0.IsOobeComplete);

        // 2. Start placement -> ActivePlacementAttemptId is set
        var started = await placement.StartPlacementTestAsync(
            userId, fx.UniversalExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
        var s1 = await learners.GetOnboardingStateAsync(userId);
        Assert.False(s1.HasCompletedPlacement);
        Assert.Equal(started.AttemptId, s1.ActivePlacementAttemptId);
        Assert.False(s1.IsOobeComplete);

        // 3. Submit placement -> HasCompletedPlacement = true, ActivePlacementAttemptId = null
        await new AttemptEngine(db).VerifyPreflightAsync(started.AttemptId, userId);
        var take = await placement.GetPlacementTakeAsync(userId, started.PlacementId, null);
        foreach (var q in take.Questions)
        {
            await placement.GetPlacementTakeAsync(userId, started.PlacementId, q.DisplayOrder);
            var opt = q.Options.Single(o => o.Label == "A").OptionId;
            await placement.SaveAnswerAsync(userId, started.PlacementId, q.QuestionId, opt, isFlagged: false);
        }
        await placement.SubmitPlacementTestAsync(userId, started.PlacementId, isAutoSubmit: false);

        var s2 = await learners.GetOnboardingStateAsync(userId);
        Assert.True(s2.HasCompletedPlacement);
        Assert.Null(s2.ActivePlacementAttemptId);
        Assert.False(s2.HasTargetGoal);
        Assert.False(s2.IsOobeComplete);

        // 4. Set goal -> HasTargetGoal = true, still !IsOobeComplete until learning path active
        await placement.SetGoalAsync(userId, null, "C1", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)), 8m);
        var s3 = await learners.GetOnboardingStateAsync(userId);
        Assert.True(s3.HasCompletedPlacement);
        Assert.True(s3.HasTargetGoal);
        Assert.False(s3.HasActiveLearningPath);
        Assert.False(s3.IsOobeComplete);

        // 5. Active Learning Path -> IsOobeComplete = true
        await SqlAsync($"""
            INSERT INTO LearnerPaths (UserId, CertId, PlacementId, CurrentBandCode, Status, StartedAt)
            VALUES ({userId}, {fx.ToeicCertId}, {started.PlacementId}, 'B2', 'active', SYSDATETIMEOFFSET());
            """);

        var s4 = await learners.GetOnboardingStateAsync(userId);
        Assert.True(s4.HasActiveLearningPath);
        Assert.True(s4.IsOobeComplete);
    }
}
