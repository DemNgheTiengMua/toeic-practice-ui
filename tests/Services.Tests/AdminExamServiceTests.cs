using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminExamServiceTests(DatabaseFixture database)
{
    private AdminExamService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AdminExamService(db, TimeProvider.System);
    }

    private async Task<(int certId, int secListeningId, int secReadingId, int secSpeakingId, int secWritingId)> SeedCertificateWithSectionsAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'L_' + @certStr, N'Listening', 'listening', 1),
              (@cert, 'R_' + @certStr, N'Reading', 'reading', 2),
              (@cert, 'S_' + @certStr, N'Speaking', 'speaking', 3),
              (@cert, 'W_' + @certStr, N'Writing', 'writing', 4);

            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'L_' + @certStr);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'R_' + @certStr);
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'S_' + @certStr);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'W_' + @certStr);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SL_' + @certStr, N'Listening Part 1', 1),
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Part 5', 2),
              (@sS, @cert, 'speaking', 'SS_' + @certStr, N'Speaking Part 1', 3),
              (@sW, @cert, 'writing', 'SW_' + @certStr, N'Writing Part 1', 4);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var secL = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "listening");
        var secR = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "reading");
        var secS = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "speaking");
        var secW = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "writing");

        return (cert.CertId, secL.SectionId, secR.SectionId, secS.SectionId, secW.SectionId);
    }

    [Fact]
    public async Task McqQuestionValidation_ValidatesOptionsAndTimingAndWords()
    {
        var (certId, secL, secR, _, _) = await SeedCertificateWithSectionsAsync($"MCQ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"MCQ Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));

        // 1. Less than 2 options throws validation error
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Stem 1",
            3,
            QuestionTypeDto.Mcq,
            Options: [new AdminQuestionOptionInput(null, "A", "Opt A", true, null)])));
        Assert.Equal(ApiError.Codes.Validation, ex1.Code);

        // 2. 0 correct options throws validation error
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Stem 2",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", false, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ])));
        Assert.Equal(ApiError.Codes.Validation, ex2.Code);

        // 3. More than 1 correct option throws validation error
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Stem 3",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", true, null)
            ])));
        Assert.Equal(ApiError.Codes.Validation, ex3.Code);

        // 4. MCQ with PrepSeconds or ResponseSeconds throws validation error
        var ex4 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Stem 4",
            3,
            QuestionTypeDto.Mcq,
            PrepSeconds: 15,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ])));
        Assert.Equal(ApiError.Codes.Validation, ex4.Code);

        // 5. MCQ with MinWords or MaxWords throws validation error
        var ex5 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Stem 5",
            3,
            QuestionTypeDto.Mcq,
            MinWords: 50,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ])));
        Assert.Equal(ApiError.Codes.Validation, ex5.Code);

        // 6. Valid MCQ succeeds
        var validMcq = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Valid MCQ Stem",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Option A", true, "Rationale A"),
                new AdminQuestionOptionInput(null, "B", "Option B", false, "Rationale B"),
                new AdminQuestionOptionInput(null, "C", "Option C", false, "Rationale C"),
                new AdminQuestionOptionInput(null, "D", "Option D", false, "Rationale D")
            ]));

        Assert.NotNull(validMcq);
        Assert.Equal("Valid MCQ Stem", validMcq.Stem);
        Assert.Equal(4, validMcq.Options.Count);
        Assert.Single(validMcq.Options, o => o.IsCorrect);
    }

    [Fact]
    public async Task FreeResponseValidation_ValidatesTimingWordsAndNoOptions()
    {
        var (certId, _, _, _, secW) = await SeedCertificateWithSectionsAsync($"FR_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"FR Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.WritingPractice));

        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secW));

        // 1. Free response cannot have options
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "FR Stem 1",
            3,
            QuestionTypeDto.FreeResponse,
            Options: [new AdminQuestionOptionInput(null, "A", "Opt A", true, null)])));
        Assert.Equal(ApiError.Codes.Validation, ex1.Code);

        // 2. ResponseSeconds must be > 0
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "FR Stem 2",
            3,
            QuestionTypeDto.FreeResponse,
            ResponseSeconds: 0)));
        Assert.Equal(ApiError.Codes.Validation, ex2.Code);

        // 3. MaxWords < MinWords throws validation error
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "FR Stem 3",
            3,
            QuestionTypeDto.FreeResponse,
            MinWords: 150,
            MaxWords: 100)));
        Assert.Equal(ApiError.Codes.Validation, ex3.Code);

        // 4. Valid Free Response succeeds
        var validFr = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Write an essay about technology.",
            4,
            QuestionTypeDto.FreeResponse,
            PrepSeconds: 60,
            ResponseSeconds: 1800,
            MinWords: 150,
            MaxWords: 300,
            Hint: "Consider advantages and disadvantages."));

        Assert.NotNull(validFr);
        Assert.Equal(QuestionTypeDto.FreeResponse, validFr.QuestionType);
        Assert.Empty(validFr.Options);
        Assert.Equal(150, validFr.MinWords);
        Assert.Equal(300, validFr.MaxWords);
    }

    [Fact]
    public async Task QuestionGroupValidation_ImageAlt_AudioSlice_And_Modality()
    {
        var (certId, secL, secR, secS, secW) = await SeedCertificateWithSectionsAsync($"GRP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var mockExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Mock Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var speakingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Speaking Test {Guid.NewGuid():N}"[..25],
            30,
            ExamPurposeDto.SpeakingPractice));

        // 1. ImagePath without ImageAlt throws error
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionGroupAsync(mockExam.ExamId, new CreateQuestionGroupRequest(
            secR,
            ImagePath: "img/test.jpg",
            ImageAlt: null)));
        Assert.Equal(ApiError.Codes.Validation, ex1.Code);

        // 2. AudioStartMs without AudioEndMs throws error
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionGroupAsync(mockExam.ExamId, new CreateQuestionGroupRequest(
            secL,
            AudioStartMs: 1000,
            AudioEndMs: null)));
        Assert.Equal(ApiError.Codes.Validation, ex2.Code);

        // 3. AudioEndMs <= AudioStartMs throws error
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionGroupAsync(mockExam.ExamId, new CreateQuestionGroupRequest(
            secL,
            AudioStartMs: 5000,
            AudioEndMs: 4000)));
        Assert.Equal(ApiError.Codes.Validation, ex3.Code);

        // 4. Speaking exam rejects reading section
        var ex4 = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateQuestionGroupAsync(speakingExam.ExamId, new CreateQuestionGroupRequest(
            secR)));
        Assert.Equal(ApiError.Codes.Validation, ex4.Code);

        // 5. Valid group succeeds
        var validGrp = await service.CreateQuestionGroupAsync(mockExam.ExamId, new CreateQuestionGroupRequest(
            secL,
            ImagePath: "img/q1.jpg",
            ImageAlt: "A desk with a laptop",
            AudioStartMs: 1000,
            AudioEndMs: 8000));
        Assert.NotNull(validGrp);
        Assert.Equal("A desk with a laptop", validGrp.ImageAlt);
    }

    [Fact]
    public async Task PublishExam_IncompleteExam_FailsWithDetails_CompleteExam_Succeeds()
    {
        var (certId, secL, secR, _, _) = await SeedCertificateWithSectionsAsync($"PUB_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Publish Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock,
            QuestionCount: 2));

        // 1. Exam with 0 questions fails publishing
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex1.Code);

        // Add 1 question in reading section
        var grpR = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));
        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grpR.GroupId,
            "Question 1 Stem",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Correct", true, null),
                new AdminQuestionOptionInput(null, "B", "Wrong", false, null)
            ]));

        // 2. QuestionCount requirement is 2, but only 1 question present -> fails
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex2.Code);

        // Add 2nd question in listening section
        var grpL = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secL));
        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grpL.GroupId,
            "Question 2 Listening Stem",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ]));

        // 3. Listening section present, but audio is missing -> fails
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex3.Code);
        Assert.Contains("audio", ex3.Message, StringComparison.OrdinalIgnoreCase);

        // Upload listening audio
        await service.SaveSectionAudioAsync(exam.ExamId, new SaveExamSectionAudioRequest(
            secL,
            "audio/test_part1.mp3",
            180,
            2048576));

        // 4. Now exam is complete -> publish succeeds!
        var publishResult = await service.PublishExamAsync(exam.ExamId);
        Assert.True(publishResult.Success);

        var updatedExam = await service.GetExamByIdAsync(exam.ExamId);
        Assert.Equal(ExamStatusDto.Published, updatedExam.Status);
    }

    [Fact]
    public async Task DeleteQuestion_BlockedWhenAttemptAnswersExist_SucceedsWhenUnattempted()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"DEL_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Attempted Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock,
            Status: ExamStatusDto.Published));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));
        var q1 = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 1 (will be answered)",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Correct", true, null),
                new AdminQuestionOptionInput(null, "B", "Wrong", false, null)
            ]));

        var q2 = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 2 (unattempted)",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ]));

        // Record learner attempt and answer for q1
        var userEmail = $"{Guid.NewGuid():N}@deltest.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Learner Tester');
            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES (@userId, {exam.ExamId}, {certId}, 'submitted', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            DECLARE @attemptId INT = SCOPE_IDENTITY();

            INSERT INTO AttemptAnswers (AttemptId, QuestionId, ExamId, SelectedOptionId, IsCorrect, IsFlagged)
            VALUES (@attemptId, {q1.QuestionId}, {exam.ExamId}, {q1.Options.First().OptionId}, 1, 0);
            """);

        // 1. Deleting q1 is blocked because it has recorded attempt answers
        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.DeleteQuestionAsync(q1.QuestionId));
        Assert.Equal(ApiError.Codes.QuestionHasAttemptAnswers, ex.Code);

        // 2. Deleting q2 (unattempted) succeeds
        await service.DeleteQuestionAsync(q2.QuestionId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var q2Exists = await db.Questions.AnyAsync(q => q.QuestionId == q2.QuestionId);
        Assert.False(q2Exists);
    }

    [Fact]
    public async Task DeleteExam_BlockedWhenFinishedAttemptsExist_CanSoftDeleteOtherwise()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"EXAM_DEL_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Finished Attempt Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock,
            Status: ExamStatusDto.Published));

        var unattemptedExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Unattempted Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // Create a finished attempt on exam
        var userEmail = $"{Guid.NewGuid():N}@finished.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Finished Learner');
            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES (@userId, {exam.ExamId}, {certId}, 'graded', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            """);

        // 1. Deleting exam with finished attempts is blocked
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.DeleteExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex1.Code);

        // 2. Changing purpose or certificate of exam with finished attempts is blocked
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamAsync(exam.ExamId, new UpdateAdminExamRequest(
            certId,
            exam.Name,
            exam.DurationMinutes,
            ExamPurposeDto.Placement,
            ExamStatusDto.Published)));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex2.Code);

        // 3. Deleting unattempted exam succeeds
        await service.DeleteExamAsync(unattemptedExam.ExamId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbExam = await db.Exams.SingleAsync(e => e.ExamId == unattemptedExam.ExamId);
        Assert.True(dbExam.IsDeleted);
    }

    [Fact]
    public async Task CreateExam_RejectsDuplicateNameInSameCertificate()
    {
        var (certId, _, _, _, _) = await SeedCertificateWithSectionsAsync($"DUP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var examName = $"Unique Exam Name {Guid.NewGuid():N}"[..25];
        await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            examName,
            90,
            ExamPurposeDto.Mock));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            examName,
            90,
            ExamPurposeDto.Mock)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateQuestion_SwitchingCorrectOption_SucceedsWithoutUniqueConstraintViolation()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"SWOPT_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Switch Option Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));
        var q = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "MCQ to switch correct option",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Text A", true, null),
                new AdminQuestionOptionInput(null, "B", "Text B", false, null)
            ]));

        // Switch correct option from A to B
        var updated = await service.UpdateQuestionAsync(q.QuestionId, new UpdateQuestionRequest(
            grp.GroupId,
            "MCQ with switched correct option",
            3,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Text A", false, null),
                new AdminQuestionOptionInput(null, "B", "Text B", true, null)
            ]));

        Assert.Equal(2, updated.Options.Count);
        Assert.False(updated.Options.Single(o => o.Label == "A").IsCorrect);
        Assert.True(updated.Options.Single(o => o.Label == "B").IsCorrect);
    }

    [Fact]
    public async Task UpdateQuestion_WhenDisplayOrderIsZeroOrNotProvided_PreservesExistingDisplayOrderAndUpdatesExplanation()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"UQ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"UQ Exam {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR, DisplayOrder: 1));

        var q = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Initial stem",
            2,
            QuestionTypeDto.Mcq,
            Explanation: "Initial explanation",
            DisplayOrder: 5,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Option A", true, null),
                new AdminQuestionOptionInput(null, "B", "Option B", false, null)
            ]));

        Assert.Equal(5, q.DisplayOrder);
        Assert.Equal("Initial explanation", q.Explanation);

        // Update with DisplayOrder = 0 (default in DTO when not explicitly provided)
        var updated = await service.UpdateQuestionAsync(q.QuestionId, new UpdateQuestionRequest(
            grp.GroupId,
            "Initial stem",
            2,
            QuestionTypeDto.Mcq,
            Explanation: "Use 'before' directly in front of a day of the week to denote a deadline.",
            DisplayOrder: 0,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Option A", true, null),
                new AdminQuestionOptionInput(null, "B", "Option B", false, null)
            ]));

        Assert.Equal(5, updated.DisplayOrder);
        Assert.Equal("Use 'before' directly in front of a day of the week to denote a deadline.", updated.Explanation);
    }

    [Fact]
    public async Task UpdateExam_BlockedWhenChangingCertificateWithExistingContent()
    {
        var (cert1Id, _, sec1R, _, _) = await SeedCertificateWithSectionsAsync($"C1_{Guid.NewGuid():N}"[..18]);
        var (cert2Id, _, _, _, _) = await SeedCertificateWithSectionsAsync($"C2_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert1Id,
            $"Cert Change Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sec1R));

        // Changing certId when exam has question groups is blocked
        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamAsync(exam.ExamId, new UpdateAdminExamRequest(
            cert2Id,
            exam.Name,
            exam.DurationMinutes,
            ExamPurposeDto.Mock,
            ExamStatusDto.Draft)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("Cannot change certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteExam_BlockedWhenInProgressAttemptsExist()
    {
        var (certId, _, _, _, _) = await SeedCertificateWithSectionsAsync($"INP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"In-Progress Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock,
            Status: ExamStatusDto.Published));

        var userEmail = $"{Guid.NewGuid():N}@inprogress.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Active Learner');
            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES (@userId, {exam.ExamId}, {certId}, 'in_progress', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            """);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.DeleteExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("in progress", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSectionAudio_RejectsNonListeningSection()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"AUD_MOD_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Audio Modality Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.SaveSectionAudioAsync(exam.ExamId, new SaveExamSectionAudioRequest(
            secR,
            "audio/reading.mp3",
            120,
            1048576)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("listening", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitQuickIngest_AppendMode_AppendsQuestionsWithIncrementedOrder()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"QI_APP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Quick Ingest Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var q1 = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Initial question stem",
            DifficultyLevel: 3,
            Explanation: "Initial explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Option A", true),
                new QuickIngestParsedOptionDto("B", "Option B", false)
            ],
            Issues: [],
            IsValid: true);

        var commit1 = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [q1]));

        Assert.True(commit1.Success);
        Assert.Equal(1, commit1.QuestionsCommitted);

        // Now append a second batch
        var q2 = new QuickIngestParsedQuestionDto(
            Index: 2,
            StartLineNumber: 6,
            EndLineNumber: 10,
            Stem: "Second question stem",
            DifficultyLevel: 4,
            Explanation: "Second explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Option 2A", false),
                new QuickIngestParsedOptionDto("B", "Option 2B", true)
            ],
            Issues: [],
            IsValid: true);

        var commit2 = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [q2]));

        Assert.True(commit2.Success);
        Assert.Equal(1, commit2.QuestionsCommitted);

        var examQuestions = await service.GetExamQuestionsAsync(exam.ExamId);
        var questions = examQuestions.Groups.Where(g => g.SectionId == secR).SelectMany(g => g.Questions).OrderBy(q => q.DisplayOrder).ToList();
        Assert.Equal(2, questions.Count);
        Assert.Equal("Initial question stem", questions[0].Stem);
        Assert.Equal(1, questions[0].DisplayOrder);
        Assert.Equal("Second question stem", questions[1].Stem);
        Assert.Equal(2, questions[1].DisplayOrder);
    }

    [Fact]
    public async Task CommitQuickIngest_ReplaceMode_ReplacesExistingQuestionsAtomically()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"QI_REP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Quick Ingest Replace {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var qOld = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Old question to be replaced",
            DifficultyLevel: 2,
            Explanation: null,
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Old A", true),
                new QuickIngestParsedOptionDto("B", "Old B", false)
            ],
            Issues: [],
            IsValid: true);

        await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [qOld]));

        // Replace with new question
        var qNew = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Brand new replacement question",
            DifficultyLevel: 3,
            Explanation: "New explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "New A", false),
                new QuickIngestParsedOptionDto("B", "New B", true)
            ],
            Issues: [],
            IsValid: true);

        var replaceCommit = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "replace",
            Questions: [qNew]));

        Assert.True(replaceCommit.Success);

        var examQuestions = await service.GetExamQuestionsAsync(exam.ExamId);
        var questions = examQuestions.Groups.Where(g => g.SectionId == secR).SelectMany(g => g.Questions).ToList();
        Assert.Single(questions);
        Assert.Equal("Brand new replacement question", questions[0].Stem);
        Assert.Equal(1, questions[0].DisplayOrder);
    }

    [Fact]
    public async Task GetExamStudioAsync_ReturnsSectionsGroupsQuestionsAndAudit()
    {
        var (certId, secL, secR, _, _) = await SeedCertificateWithSectionsAsync($"STUDIO_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Studio Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));
        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Studio Question 1",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ]));

        var studio = await service.GetExamStudioAsync(exam.ExamId);

        Assert.NotNull(studio);
        Assert.Equal(exam.ExamId, studio.ExamId);
        Assert.NotEmpty(studio.Sections);
        Assert.NotNull(studio.Audit);

        var rSection = studio.Sections.FirstOrDefault(s => s.SectionId == secR);
        Assert.NotNull(rSection);
        Assert.Single(rSection.Groups);
        Assert.Single(rSection.Groups[0].Questions);
        Assert.Equal("Studio Question 1", rSection.Groups[0].Questions[0].Stem);
    }

    [Fact]
    public async Task ReorderSectionItemsAsync_UpdatesDisplayOrder()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"REORDER_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Reorder Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR));
        var q1 = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 1",
            1,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "A", true, null),
                new AdminQuestionOptionInput(null, "B", "B", false, null)
            ]));

        var q2 = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 2",
            1,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "A", true, null),
                new AdminQuestionOptionInput(null, "B", "B", false, null)
            ]));

        // Reorder q2 first, q1 second
        var reorderReq = new ReorderSectionItemsRequest(
            SectionId: secR,
            Items:
            [
                new ReorderSectionItemEntry(null, q2.QuestionId, 1),
                new ReorderSectionItemEntry(null, q1.QuestionId, 2)
            ]);

        await service.ReorderSectionItemsAsync(exam.ExamId, reorderReq);

        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var questions = studio.Sections.First(s => s.SectionId == secR).Groups[0].Questions;
        Assert.Equal(q2.QuestionId, questions[0].QuestionId);
        Assert.Equal(1, questions[0].DisplayOrder);
        Assert.Equal(q1.QuestionId, questions[1].QuestionId);
        Assert.Equal(2, questions[1].DisplayOrder);
    }

    [Fact]
    public async Task CreateExamClusterAsync_P3Conversation_CreatesGroupAnd3ChildQuestions()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"CLUSTER_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Cluster Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var cluster = await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: null,
            Transcript: "M: Hello Sarah\nW: Hi Bob",
            AudioPath: "/audio/p3-test.mp3",
            AudioStartMs: 1000,
            AudioEndMs: 35000,
            QuestionCount: 3));

        Assert.NotNull(cluster);
        Assert.Equal("M: Hello Sarah\nW: Hi Bob", cluster.Transcript);
        Assert.Equal("/audio/p3-test.mp3", cluster.AudioPath);
        Assert.Equal(1000, cluster.AudioStartMs);
        Assert.Equal(35000, cluster.AudioEndMs);
        Assert.Equal(3, cluster.Questions.Count);
        foreach (var q in cluster.Questions)
        {
            Assert.Equal(4, q.Options.Count);
            Assert.Contains(q.Options, o => o.Label == "A" && o.IsCorrect);
        }
    }

    [Fact]
    public async Task CreateExamClusterAsync_SpeakingWriting_SetsFreeResponseAndTimers()
    {
        var (certId, _, _, secS, secW) = await SeedCertificateWithSectionsAsync($"SW_CLUS_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Speaking Cluster {Guid.NewGuid():N}"[..25],
            30,
            ExamPurposeDto.SpeakingPractice));

        var cluster = await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secS,
            QuestionCount: 1));

        Assert.NotNull(cluster);
        Assert.Single(cluster.Questions);
        var q = cluster.Questions[0];
        Assert.Equal(QuestionTypeDto.FreeResponse, q.QuestionType);
        Assert.Empty(q.Options);
        Assert.Equal(15, q.PrepSeconds);
        Assert.Equal(45, q.ResponseSeconds);
    }

    [Fact]
    public async Task ExportExamPackageAsync_JsonFormat_ReturnsManifestStream()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"EXPJ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Export Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: "Export passage",
            QuestionCount: 2));

        var (stream, contentType, fileName) = await service.ExportExamPackageAsync(exam.ExamId, "json");

        Assert.Equal("application/json", contentType);
        Assert.EndsWith("_manifest.json", fileName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal(exam.Name, root.GetProperty("exam").GetProperty("title").GetString());
        Assert.True(root.GetProperty("sections").GetArrayLength() > 0);
        var firstSec = root.GetProperty("sections")[0];
        Assert.True(firstSec.GetProperty("groups").GetArrayLength() > 0);
        Assert.Equal(2, firstSec.GetProperty("groups")[0].GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public async Task ExportExamPackageAsync_ZipFormat_ContainsManifestEntry()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"EXPZ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Export Zip {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: "Zip passage",
            QuestionCount: 1));

        var (stream, contentType, fileName) = await service.ExportExamPackageAsync(exam.ExamId, "zip");

        Assert.Equal("application/zip", contentType);
        Assert.EndsWith("_package.zip", fileName);
        Assert.NotNull(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        using var entryStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(entryStream);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal(exam.Name, root.GetProperty("exam").GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetExamsAsync_FiltersByPurpose_AndScopesAudioStatus()
    {
        var (certId, secL, secR, secS, secW) = await SeedCertificateWithSectionsAsync($"PURP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var mockExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Mock Exam {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));

        var placementExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Placement Exam {Guid.NewGuid():N}"[..25], 45, ExamPurposeDto.Placement));

        var speakingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Speaking Exam {Guid.NewGuid():N}"[..25], 20, ExamPurposeDto.SpeakingPractice));

        var writingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Writing Exam {Guid.NewGuid():N}"[..25], 30, ExamPurposeDto.WritingPractice));

        // 1. Filter by "mock"
        var mockResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "mock", page: 1, pageSize: 20);
        Assert.Single(mockResult.Exams);
        Assert.Equal(mockExam.ExamId, mockResult.Exams[0].ExamId);
        Assert.Equal("No audio", mockResult.Exams[0].AudioStatus); // Listening section exists but no audio uploaded

        // 2. Filter by "placement"
        var placementResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "placement", page: 1, pageSize: 20);
        Assert.Single(placementResult.Exams);
        Assert.Equal(placementExam.ExamId, placementResult.Exams[0].ExamId);

        // 3. Filter by "practice" returns both speaking and writing
        var practiceResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "practice", page: 1, pageSize: 20);
        Assert.Equal(2, practiceResult.Exams.Count);
        Assert.Contains(practiceResult.Exams, e => e.ExamId == speakingExam.ExamId);
        Assert.Contains(practiceResult.Exams, e => e.ExamId == writingExam.ExamId);

        // Verify audioStatus scoping: Speaking and Writing practice exams are N/A
        var speakingItem = practiceResult.Exams.Single(e => e.ExamId == speakingExam.ExamId);
        var writingItem = practiceResult.Exams.Single(e => e.ExamId == writingExam.ExamId);
        Assert.Equal("N/A", speakingItem.AudioStatus);
        Assert.Equal("N/A", writingItem.AudioStatus);
    }

    [Fact]
    public async Task Audit_blocks_publish_when_a_listening_section_master_track_is_not_sliced_per_group()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"AUD75_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Audio Audit {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));
        var g1 = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secL));
        var g2 = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secL, DisplayOrder: 2));
        await database.SqlAsync($"""
            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
            VALUES ({exam.ExamId}, {secL}, {certId}, N'/media/exams/{exam.ExamId}/master.mp3', 600, 1000);
            """);

        // Section audio alone used to pass; every cluster would play the whole master track.
        var unsliced = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.Equal(
            [g1.GroupId, g2.GroupId],
            unsliced.Issues.Where(i => i.Code == "GRP_AUDIO_UNSLICED" && i.Severity == "error").Select(i => i.GroupId!.Value).Order());

        await service.UpdateQuestionGroupAsync(exam.ExamId, g1.GroupId, new UpdateQuestionGroupRequest(secL, AudioStartMs: 0, AudioEndMs: 4000, DisplayOrder: 1));
        await service.UpdateQuestionGroupAsync(exam.ExamId, g2.GroupId, new UpdateQuestionGroupRequest(secL, AudioStartMs: 4000, AudioEndMs: 9000, DisplayOrder: 2));

        var sliced = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(sliced.Issues, i => i.Code is "GRP_AUDIO_UNSLICED" or "SEC_AUDIO_MISSING");
    }

    [Fact]
    public async Task Audit_warns_when_a_section_master_track_overrides_a_groups_own_audio_file()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"AUDOV_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Audio Override {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));
        var own = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secL, AudioPath: "/audio/own-file.mp3"));
        await database.SqlAsync($"""
            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
            VALUES ({exam.ExamId}, {secL}, {certId}, N'/media/exams/{exam.ExamId}/master.mp3', 600, 1000);
            """);

        var audit = await service.AuditExamPublishReadinessAsync(exam.ExamId);

        var issue = Assert.Single(audit.Issues, i => i.Code == "GRP_AUDIO_OVERRIDDEN");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal(own.GroupId, issue.GroupId);

        // A group whose own file is the master track itself (seeded exams) is not overridden.
        await service.UpdateQuestionGroupAsync(exam.ExamId, own.GroupId, new UpdateQuestionGroupRequest(secL, AudioPath: $"/media/exams/{exam.ExamId}/master.mp3"));
        var same = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(same.Issues, i => i.Code == "GRP_AUDIO_OVERRIDDEN");
    }

    [Fact]
    public async Task FreeResponseQuestions_SaveSectionRubric_AndAuditWordBounds()
    {
        var (certId, _, secR, _, secW) = await SeedCertificateWithSectionsAsync($"RUB_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Rubric Audit {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.WritingPractice));
        var writingGroup = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secW));

        await database.SqlAsync($"""
            DECLARE @skillId INT = (SELECT SkillId FROM Sections WHERE SectionId = {secW});
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder)
            VALUES ({certId}, @skillId, 'TASK', N'Task achievement', 9, 1, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var criterionId = await db.GradingCriteria
            .Where(c => c.CertId == certId && c.Code == "TASK")
            .Select(c => c.CriterionId)
            .SingleAsync();

        var freeResponse = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            writingGroup.GroupId,
            "Write a response to the prompt.",
            3,
            QuestionTypeDto.FreeResponse,
            ResponseSeconds: 1200));

        var incompleteAudit = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.Contains(incompleteAudit.Issues, i => i.Code == "Q_FR_CRITERIA_MISSING" && i.QuestionId == freeResponse.QuestionId);
        Assert.Contains(incompleteAudit.Issues, i => i.Code == "Q_FR_WORD_BOUNDS" && i.QuestionId == freeResponse.QuestionId);

        await service.UpdateQuestionAsync(freeResponse.QuestionId, new UpdateQuestionRequest(
            GroupId: null,
            Stem: freeResponse.Stem,
            DifficultyLevel: freeResponse.DifficultyLevel,
            QuestionType: QuestionTypeDto.FreeResponse,
            ResponseSeconds: 1200,
            MinWords: 100,
            MaxWords: 250,
            CriterionIds: [criterionId]));

        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var studioQuestion = Assert.Single(studio.Sections
            .SelectMany(s => s.Groups)
            .SelectMany(g => g.Questions));
        Assert.Contains(studioQuestion.Criteria, c => c.CriterionId == criterionId);
        Assert.Contains(studio.AvailableCriteria, c => c.CriterionId == criterionId && c.SkillId == studioQuestion.SkillId);

        var completedAudit = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(completedAudit.Issues, i => i.Code is "Q_FR_CRITERIA_MISSING" or "Q_FR_WORD_BOUNDS");

        var mcqExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Rubric MCQ {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));
        var readingGroup = await service.CreateQuestionGroupAsync(mcqExam.ExamId, new CreateQuestionGroupRequest(secR));
        var mcq = await service.CreateQuestionAsync(mcqExam.ExamId, new CreateQuestionRequest(
            readingGroup.GroupId,
            "Choose the correct answer.",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Correct", true, null),
                new AdminQuestionOptionInput(null, "B", "Incorrect", false, null)
            ]));
        var afterMcqAudit = await service.AuditExamPublishReadinessAsync(mcqExam.ExamId);
        Assert.DoesNotContain(afterMcqAudit.Issues, i => i.Code == "Q_FR_CRITERIA_MISSING" && i.QuestionId == mcq.QuestionId);
    }
    [Fact]
    public async Task GetExamStudioAsync_PopulatesDynamicSectionBlueprintModels()
    {
        var certCode = $"BP_{Guid.NewGuid():N}"[..18];
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = $"SK_{cert.CertId}", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            SkillId = skill.SkillId,
            CertId = cert.CertId,
            Modality = "reading",
            Code = "SEC_BP",
            Name = "Blueprint Section",
            DisplayOrder = 1,
            ExpectedQuestionCount = 10,
            OptionCount = 4,
            ClusterSize = 3,
            UnitCount = 2,
            HideOptionText = true,
            RequirePassage = true,
            RequireAudio = false,
            RequireImage = true,
            RequiredKeywordCount = 3,
            AllowedComponentTypesJson = @"[""mcq"",""multi_select"",""inline_completion""]",
            LayoutType = "SPLIT_HORIZONTAL",
            NavigationMode = "UNIT_LOCKED_SANDBOX",
            TimerPolicyJson = @"{""perSectionSec"":1800}",
            ToolsConfigJson = @"{""calculator"":false}"
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId,
            $"Blueprint Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var secDto = Assert.Single(studio.Sections);

        Assert.Equal("SEC_BP", secDto.Code);
        Assert.Equal(10, secDto.ExpectedQuestionCount);
        Assert.Equal(4, secDto.OptionCount);
        Assert.Equal(3, secDto.ClusterSize);
        Assert.Equal(2, secDto.UnitCount);
        Assert.True(secDto.HideOptionText);
        Assert.True(secDto.RequirePassage);
        Assert.False(secDto.RequireAudio);
        Assert.True(secDto.RequireImage);
        Assert.Equal(3, secDto.RequiredKeywordCount);
        Assert.Equal("SPLIT_HORIZONTAL", secDto.LayoutType);
        Assert.Equal("UNIT_LOCKED_SANDBOX", secDto.NavigationMode);
        Assert.Equal(@"{""perSectionSec"":1800}", secDto.TimerPolicyJson);
        Assert.Equal(@"{""calculator"":false}", secDto.ToolsConfigJson);
        Assert.Equal("Reading", secDto.SkillName);
    }

    [Fact]
    public async Task AuthoringQuestionComponentTypes_PersistsAllFieldsAndOptions()
    {
        var (certId, _, secR, _, secW) = await SeedCertificateWithSectionsAsync($"AUTH_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Authoring Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secR, UnitNumber: 2));
        Assert.Equal(2, grp.UnitNumber);

        // 1. MCQ 3
        var qMcq3 = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "MCQ 3 Stem",
            2,
            QuestionTypeDto.Mcq_3,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, "Correct rationale"),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, "Distractor B"),
                new AdminQuestionOptionInput(null, "C", "Opt C", false, "Distractor C")
            ]));
        Assert.Equal(QuestionTypeDto.Mcq_3, qMcq3.QuestionType);
        Assert.Equal(3, qMcq3.Options.Count);

        // 2. MultiSelect with MaxSelections
        var qMulti = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "MultiSelect Stem",
            3,
            QuestionTypeDto.MultiSelect,
            MaxSelections: 2,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", true, null),
                new AdminQuestionOptionInput(null, "C", "Opt C", false, null),
                new AdminQuestionOptionInput(null, "D", "Opt D", false, null)
            ]));
        Assert.Equal(QuestionTypeDto.MultiSelect, qMulti.QuestionType);
        Assert.Equal(2, qMulti.MaxSelections);
        Assert.Equal(2, qMulti.Options.Count(o => o.IsCorrect));

        // 3. InlineCompletion with AcceptedAnswersJson and AllowNumber
        var qInline = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Inline Stem",
            3,
            QuestionTypeDto.InlineCompletion,
            AcceptedAnswersJson: @"[""center"",""centre""]",
            AllowNumber: false,
            MaxWords: 2));
        Assert.Equal(QuestionTypeDto.InlineCompletion, qInline.QuestionType);
        Assert.Equal(@"[""center"",""centre""]", qInline.AcceptedAnswersJson);
        Assert.False(qInline.AllowNumber);
        Assert.Equal(2, qInline.MaxWords);

        // 4. FreeResponse with RequiredKeywords
        var grpW = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(secW));
        var qFree = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grpW.GroupId,
            "Free Response Essay",
            3,
            QuestionTypeDto.FreeResponse,
            PrepSeconds: 30,
            ResponseSeconds: 1200,
            MinWords: 150,
            MaxWords: 300,
            RequiredKeywords: "climate, renewable, energy"));
        Assert.Equal(QuestionTypeDto.FreeResponse, qFree.QuestionType);
        Assert.Equal("climate, renewable, energy", qFree.RequiredKeywords);
        Assert.Equal(150, qFree.MinWords);
        Assert.Equal(300, qFree.MaxWords);

        // Reload via GetExamStudioAsync
        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var allQs = studio.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Questions).ToList();
        Assert.Contains(allQs, q => q.QuestionType == QuestionTypeDto.Mcq_3 && q.Options.Count == 3);
        Assert.Contains(allQs, q => q.QuestionType == QuestionTypeDto.MultiSelect && q.MaxSelections == 2);
        Assert.Contains(allQs, q => q.QuestionType == QuestionTypeDto.InlineCompletion && q.AcceptedAnswersJson == @"[""center"",""centre""]");
        Assert.Contains(allQs, q => q.QuestionType == QuestionTypeDto.FreeResponse && q.RequiredKeywords == "climate, renewable, energy");
    }

    [Fact]
    public async Task AuditExamPublishReadiness_DynamicBlueprintValidation()
    {
        var certCode = $"AUD_{Guid.NewGuid():N}"[..18];
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = $"SK_{cert.CertId}", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            SkillId = skill.SkillId,
            CertId = cert.CertId,
            Modality = "reading",
            Code = "SEC_AUD",
            Name = "Audit Section",
            DisplayOrder = 1,
            ExpectedQuestionCount = 2,
            OptionCount = 2,
            ClusterSize = 2,
            RequirePassage = true,
            RequireImage = true,
            AllowedComponentTypesJson = @"[""mcq""]"
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId,
            $"Audit Test Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // 1. Cluster size mismatch (requires 2 questions per cluster, but only 1 added)
        // AND RequirePassage missing, AND RequireImage missing
        var grp = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(section.SectionId));
        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 1",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ]));

        var audit1 = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.False(audit1.CanPublish);
        Assert.Contains(audit1.Issues, i => i.Code == "SEC_QUESTION_COUNT_LOW");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_CLUSTER_SIZE_MISMATCH");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_PASSAGE_MISSING");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_IMAGE_MISSING");

        // 2. Fix group: add passage, image, image alt, and 2nd question
        await service.UpdateQuestionGroupAsync(exam.ExamId, grp.GroupId, new UpdateQuestionGroupRequest(
            section.SectionId,
            Passage: "Reading passage stimulus text here.",
            ImagePath: "/media/photo.jpg",
            ImageAlt: "Photo of office workers"));

        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            grp.GroupId,
            "Question 2",
            2,
            QuestionTypeDto.Mcq,
            Options:
            [
                new AdminQuestionOptionInput(null, "A", "Opt A", true, null),
                new AdminQuestionOptionInput(null, "B", "Opt B", false, null)
            ]));

        var audit2 = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.True(audit2.CanPublish, string.Join("; ", audit2.Issues.Select(i => i.Message)));
    }
}
