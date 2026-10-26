using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ScoreConversionServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private ScoreConversionService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new ScoreConversionService(db);
    }

    private async Task<(int certId, int skillId)> SeedCertificateAndSkillAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cId INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cId, 'LIST', N'Listening', 'listening', 1);
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cId, 'LOW', N'Low Band', 0, 499, 1),
              (@cId, 'HIGH', N'High Band', 500, 990, 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skill = await db.Skills.SingleAsync(s => s.CertId == cert.CertId && s.Code == "LIST");
        return (cert.CertId, skill.SkillId);
    }

    [Fact]
    public async Task GetScoreConversion_Returns_Certificates_Scales_And_Bands()
    {
        var certCode = Unique("CONV");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);
        var service = CreateService();

        var view = await service.GetScoreConversionAsync(certCode);

        Assert.Equal(certCode, view.CertCode);
        Assert.Equal(certId, view.CertId);
        Assert.NotEmpty(view.Certificates);
        Assert.Single(view.SkillScales);
        Assert.Equal(2, view.Bands.Count);
        Assert.Equal("LOW", view.Bands[0].Code);
        Assert.Equal("HIGH", view.Bands[1].Code);
    }

    [Fact]
    public async Task UpdateScoreConversion_Rejects_Non_Monotonic_Scales()
    {
        var certCode = Unique("MONO");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);
        var service = CreateService();

        var command = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales:
            [
                new UpdateScoreScaleEntry(skillId, RawScore: 0, ScaledScore: 100),
                new UpdateScoreScaleEntry(skillId, RawScore: 1, ScaledScore: 50), // non-monotonic: 50 < 100
                new UpdateScoreScaleEntry(skillId, RawScore: 2, ScaledScore: 200)
            ],
            Bands: []
        );

        var ex = await Assert.ThrowsAsync<ScoreConversionException>(
            () => service.UpdateScoreConversionAsync(certId, command));

        Assert.Equal(ScoreConversionException.NonMonotonicScales, ex.Code);
    }

    [Fact]
    public async Task UpdateScoreConversion_Rejects_Overlapping_Bands()
    {
        var certCode = Unique("OVER");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);
        var service = CreateService();

        var command = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales: [],
            Bands:
            [
                new UpdateScoreBandEntry(null, "B1", "Band 1", MinTotal: 0, MaxTotal: 500, DisplayOrder: 1),
                new UpdateScoreBandEntry(null, "B2", "Band 2", MinTotal: 450, MaxTotal: 990, DisplayOrder: 2) // overlaps 450 <= 500
            ]
        );

        var ex = await Assert.ThrowsAsync<ScoreConversionException>(
            () => service.UpdateScoreConversionAsync(certId, command));

        Assert.Equal(ScoreConversionException.OverlappingBands, ex.Code);
    }

    [Fact]
    public async Task UpdateScoreConversion_Rejects_Min_Greater_Than_Max()
    {
        var certCode = Unique("INV");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);
        var service = CreateService();

        var command = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales: [],
            Bands:
            [
                new UpdateScoreBandEntry(null, "BAD", "Bad Band", MinTotal: 500, MaxTotal: 400, DisplayOrder: 1)
            ]
        );

        var ex = await Assert.ThrowsAsync<ScoreConversionException>(
            () => service.UpdateScoreConversionAsync(certId, command));

        Assert.Equal(ScoreConversionException.InvalidBandRange, ex.Code);
    }

    [Fact]
    public async Task UpdateScoreConversion_Rejects_Non_Monotonic_DisplayOrder()
    {
        var certCode = Unique("ORD");
        var (certId, _) = await SeedCertificateAndSkillAsync(certCode);
        var service = CreateService();

        var command = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales: [],
            Bands:
            [
                new UpdateScoreBandEntry(null, "B1", "Band 1", MinTotal: 0, MaxTotal: 500, DisplayOrder: 2),
                new UpdateScoreBandEntry(null, "B2", "Band 2", MinTotal: 501, MaxTotal: 990, DisplayOrder: 1) // Non-monotonic DisplayOrder
            ]
        );

        var ex = await Assert.ThrowsAsync<ScoreConversionException>(
            () => service.UpdateScoreConversionAsync(certId, command));

        Assert.Equal(ScoreConversionException.InvalidData, ex.Code);
    }

    [Fact]
    public async Task UpdateScoreConversion_EmptyScalesList_DoesNotWipeExistingScales()
    {
        var certCode = Unique("WIPE");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);
        await database.SqlAsync($"""
            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore) VALUES
              ({certId}, {skillId}, 0, 100),
              ({certId}, {skillId}, 1, 200);
            """);

        var service = CreateService();

        // Update bands only, passing Scales: []
        var command = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales: [],
            Bands:
            [
                new UpdateScoreBandEntry(null, "LOW", "Low Band Updated", MinTotal: 0, MaxTotal: 599, DisplayOrder: 1),
                new UpdateScoreBandEntry(null, "HIGH", "High Band Updated", MinTotal: 600, MaxTotal: 990, DisplayOrder: 2)
            ]
        );

        var view = await service.UpdateScoreConversionAsync(certId, command);

        // Verify that existing scales are NOT wiped
        var skillGroup = Assert.Single(view.SkillScales);
        Assert.Equal(2, skillGroup.Scales.Count);
        Assert.Equal(100, skillGroup.Scales[0].ScaledScore);
        Assert.Equal(200, skillGroup.Scales[1].ScaledScore);
    }

    [Fact]
    public async Task Previously_Scored_Attempt_Preserves_Snapshotted_Band_After_Admin_Edit()
    {
        var certCode = Unique("SNAP");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2);
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            -- Original bands: LOW (0-499), HIGH (500-990)
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low Band', 0, 499, 1),
              (@cert, 'HIGH', N'High Band', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Mock {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sL, @exam, 0, 100),
              (@cert, @sL, @exam, 1, 400),
              (@cert, @sR, @exam, 0, 100),
              (@cert, @sR, @exam, 1, 400);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @exam, @secL, @sL, N'Listening Q1', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @exam, @secR, @sR, N'Reading Q2', 1, 'mcq', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q2, 'mcq', 'A', N'Opt A', 0),
              (@q2, 'mcq', 'B', N'Opt B', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var q1 = await db.Questions.FirstAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 1);
        var q2 = await db.Questions.FirstAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 2);
        var opt1 = await db.QuestionOptions.FirstAsync(o => o.QuestionId == q1.QuestionId && o.IsCorrect);
        var opt2 = await db.QuestionOptions.FirstAsync(o => o.QuestionId == q2.QuestionId && o.IsCorrect);
        var listSkill = await db.Skills.FirstAsync(s => s.CertId == cert.CertId && s.Code == "LIST");
        var readSkill = await db.Skills.FirstAsync(s => s.CertId == cert.CertId && s.Code == "READ");

        // Seed learner and take exam
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner', {cert.CertId});
            DECLARE @uId INT = SCOPE_IDENTITY();
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @admId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES (@uId, 'admin_grant', 5, @admId, N'Credits');
            """);

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        var learners = new LearnerService(db);
        var mockService = new MockExamService(db, learners);

        var attempt = await mockService.StartAttemptAsync(userId, exam.ExamId);
        await mockService.SaveAnswerAsync(userId, attempt.AttemptId, q1.QuestionId, opt1.OptionId, false);
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET StartedAt = DATEADD(minute, -31, SYSDATETIMEOFFSET()),
                ExpiresAt = DATEADD(minute, 29, SYSDATETIMEOFFSET()),
                ActiveSkillId = {readSkill.SkillId},
                PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            WHERE AttemptId = {attempt.AttemptId};
            """);
        var secondPhaseDb = DbContextFactory.Create(database.ConnectionString);
        mockService = new MockExamService(secondPhaseDb, new LearnerService(secondPhaseDb));
        await mockService.SaveAnswerAsync(userId, attempt.AttemptId, q2.QuestionId, opt2.OptionId, false);
        var resultBeforeEdit = await mockService.SubmitAttemptAsync(userId, attempt.AttemptId, false);

        // Before edit: TotalScore is 800 (400 + 400), which falls in "HIGH" (500-990)
        Assert.Equal(800, resultBeforeEdit.TotalScore);
        Assert.Equal("HIGH", resultBeforeEdit.BandCode);

        // Now Admin edits the bands and scales:
        // Admin creates new band ranges:
        // Tier 1: 0-600 ("BASIC")
        // Tier 2: 601-850 ("ADVANCED")  <-- 800 would fall here under the new bands
        // Tier 3: 851-990 ("EXPERT")
        var conversionService = new ScoreConversionService(db);
        var editCommand = new UpdateScoreConversionCommand(
            ExamId: null,
            Scales:
            [
                new UpdateScoreScaleEntry(listSkill.SkillId, 0, 50),
                new UpdateScoreScaleEntry(listSkill.SkillId, 1, 450),
                new UpdateScoreScaleEntry(readSkill.SkillId, 0, 50),
                new UpdateScoreScaleEntry(readSkill.SkillId, 1, 450)
            ],
            Bands:
            [
                new UpdateScoreBandEntry(null, "BASIC", "Basic Tier", 0, 600, 1),
                new UpdateScoreBandEntry(null, "ADVANCED", "Advanced Tier", 601, 850, 2),
                new UpdateScoreBandEntry(null, "EXPERT", "Expert Tier", 851, 990, 3)
            ]
        );

        await conversionService.UpdateScoreConversionAsync(cert.CertId, editCommand);

        // Verify Acceptance Criterion 2:
        // "A previously scored attempt shows the same band after an edit"
        var resultAfterEdit = await mockService.GetAttemptResultAsync(userId, attempt.AttemptId);
        Assert.Equal("HIGH", resultAfterEdit.BandCode);
        Assert.Equal(800, resultAfterEdit.TotalScore);

        var reviewAfterEdit = await mockService.GetAttemptReviewAsync(userId, attempt.AttemptId);
        Assert.Equal("HIGH", reviewAfterEdit.BandCode);
        Assert.Equal(800, reviewAfterEdit.TotalScore);

        var historyAfterEdit = await mockService.GetExamHistoryAsync(userId);
        var attemptInHistory = historyAfterEdit.Attempts.Single(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal("HIGH", attemptInHistory.BandCode);
        Assert.Equal(800, attemptInHistory.TotalScore);
    }

    [Fact]
    public async Task GetScoreConversion_Dynamically_Sizes_MaxQuestions_From_ExpectedQuestionCount()
    {
        var certCode = Unique("DYNQ");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);

        // Add section with ExpectedQuestionCount = 45 (e.g. TOEIC CAT blueprint)
        await database.SqlAsync($"""
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount)
            VALUES ({skillId}, {certId}, 'listening', 'SEC_CAT', N'Section CAT', 1, 45);
            """);

        var service = CreateService();
        var view = await service.GetScoreConversionAsync(certCode);

        var skillGroup = view.SkillScales.Single(s => s.SkillId == skillId);
        Assert.Equal(45, skillGroup.MaxQuestions);
    }

    [Fact]
    public async Task CalculateScorePreviewAsync_Loads_Bands_And_Computes_Preview()
    {
        var certCode = Unique("PREV");
        var (certId, skillId) = await SeedCertificateAndSkillAsync(certCode);

        var service = CreateService();
        var request = new ScorePreviewRequest(
            CertId: certId,
            CertCode: certCode,
            ScoreAggregationRule: "sum",
            Skills:
            [
                new SkillScoreInput(skillId, "LIST", 100m, null)
            ]
        );

        var result = await service.CalculateScorePreviewAsync(request);
        Assert.True(result.TotalScore > 0);
        Assert.NotNull(result.BandCode);
    }
}
