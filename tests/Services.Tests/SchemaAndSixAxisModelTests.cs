using System.Text.Json;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SchemaAndSixAxisModelTests(DatabaseFixture database)
{
    private static async Task RunSqlAsync(string connectionString, string commandText)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Certificates_Supports_SixAxis_ScoreAggregationRule_And_PresetCode()
    {
        var databaseName = $"CertSixAxis_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);

            var cert = new Certificate
            {
                Code = "CUST_01",
                Name = "Custom 6-Axis Certificate",
                ScoreAggregationRule = "avg_half_step",
                PresetCode = "ielts_academic",
                IsActive = true
            };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var loaded = await db.Certificates.SingleAsync(c => c.Code == "CUST_01");
            Assert.Equal("avg_half_step", loaded.ScoreAggregationRule);
            Assert.Equal("ielts_academic", loaded.PresetCode);

            // Test check constraint CK_Certificates_ScoreAggregationRule rejects invalid values
            await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await RunSqlAsync(connectionString, """
                    INSERT INTO Certificates (Code, Name, ScoreAggregationRule)
                    VALUES ('INVALID_RULE', N'Invalid', 'invalid_rule_code');
                    """);
            });
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Triggers_Enforce_ScoreAggregationRule_Dynamically()
    {
        var databaseName = $"TriggerDynamic_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            // Seed a custom certificate with avg_half_step (NOT named IELTS)
            await RunSqlAsync(connectionString, """
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
                  INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'test@test.local', 0x01, N'Learner');
                DECLARE @userId INT = SCOPE_IDENTITY();

                INSERT INTO Certificates (Code, Name, ScoreAggregationRule, PresetCode)
                VALUES ('CUSTOM_HALF', N'Custom Half Band Cert', 'avg_half_step', 'custom');
                DECLARE @certHalf INT = SCOPE_IDENTITY();

                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
                  (@certHalf, 'S1', N'Skill 1', 'listening', 1),
                  (@certHalf, 'S2', N'Skill 2', 'reading', 2);
                DECLARE @skill1 INT = (SELECT SkillId FROM Skills WHERE CertId = @certHalf AND Code = 'S1');
                DECLARE @skill2 INT = (SELECT SkillId FROM Skills WHERE CertId = @certHalf AND Code = 'S2');

                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
                  (@skill1, @certHalf, 'listening', 'SEC1', N'Section 1', 1),
                  (@skill2, @certHalf, 'reading', 'SEC2', N'Section 2', 2);

                INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
                VALUES (@certHalf, N'Exam 1', 'published', 60, 'mock');
                DECLARE @examId INT = SCOPE_IDENTITY();

                -- Attempt with scaled scores: 60 and 65 -> Average = 62.5 -> IELTS half-step rounding = 65
                INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore)
                VALUES (@userId, @examId, @certHalf, 'in_progress', DATEADD(hour, 1, SYSDATETIMEOFFSET()), 65);
                DECLARE @attemptId INT = SCOPE_IDENTITY();

                INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
                  (@attemptId, @skill1, @certHalf, 10, 60),
                  (@attemptId, @skill2, @certHalf, 10, 65);
                """);

            // Inserting invalid TotalScore (e.g. sum = 125 instead of 65) must fail with error 50011
            var ex = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await RunSqlAsync(connectionString, """
                    DECLARE @userId INT = (SELECT TOP 1 UserId FROM Users);
                    DECLARE @examId INT = (SELECT TOP 1 ExamId FROM Exams WHERE Name = N'Exam 1');
                    DECLARE @certHalf INT = (SELECT TOP 1 CertId FROM Certificates WHERE Code = 'CUSTOM_HALF');
                    DECLARE @skill1 INT = (SELECT SkillId FROM Skills WHERE CertId = @certHalf AND Code = 'S1');

                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore)
                    VALUES (@userId, @examId, @certHalf, 'in_progress', DATEADD(hour, 1, SYSDATETIMEOFFSET()), 125);
                    DECLARE @badAttempt INT = SCOPE_IDENTITY();

                    INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
                    VALUES (@badAttempt, @skill1, @certHalf, 10, 60);
                    """);
            });
            Assert.Equal(50011, ex.Number);

            // Test independent aggregation rule: TotalScore can be anything or NULL without error
            await RunSqlAsync(connectionString, """
                DECLARE @userId INT = (SELECT TOP 1 UserId FROM Users);
                INSERT INTO Certificates (Code, Name, ScoreAggregationRule)
                VALUES ('INDEP_CERT', N'Independent Cert', 'independent');
                DECLARE @certIndep INT = SCOPE_IDENTITY();

                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
                  (@certIndep, 'S1', N'Skill 1', 'speaking', 1);
                DECLARE @skillIndep INT = SCOPE_IDENTITY();

                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
                  (@skillIndep, @certIndep, 'speaking', 'SEC1', N'Section 1', 1);

                INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
                VALUES (@certIndep, N'Indep Exam', 'published', 20, 'mock');
                DECLARE @examIndep INT = SCOPE_IDENTITY();

                INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore)
                VALUES (@userId, @examIndep, @certIndep, 'in_progress', DATEADD(hour, 1, SYSDATETIMEOFFSET()), 999);
                DECLARE @attemptIndep INT = SCOPE_IDENTITY();

                INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
                VALUES (@attemptIndep, @skillIndep, @certIndep, 10, 50);
                """);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Exams_Supports_DeliveryMode_And_ExamFamily()
    {
        var databaseName = $"ExamDelivery_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var cert = new Certificate { Code = "EX_CERT", Name = "Cert" };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId,
                Name = "Practice Mode Exam",
                DurationMinutes = 60,
                Purpose = "mock",
                DeliveryMode = "practice",
                ExamFamily = "TOEIC"
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var loaded = await db.Exams.SingleAsync(e => e.ExamId == exam.ExamId);
            Assert.Equal("practice", loaded.DeliveryMode);
            Assert.Equal("TOEIC", loaded.ExamFamily);

            // Rejects invalid DeliveryMode
            await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await RunSqlAsync(connectionString, $"""
                    INSERT INTO Exams (CertId, Name, DurationMinutes, DeliveryMode)
                    VALUES ({cert.CertId}, N'Invalid Delivery', 60, 'invalid_mode');
                    """);
            });
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Sections_Supports_SixAxis_Blueprint_Attributes()
    {
        var databaseName = $"SecSixAxis_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var cert = new Certificate { Code = "SEC_CERT", Name = "Cert" };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                CertId = cert.CertId,
                SkillId = skill.SkillId,
                Modality = "writing",
                Code = "W_TASK2",
                Name = "Writing Task 2",
                ScoreWeight = 2.0m,
                NavigationMode = "FULL_SANDBOX",
                TimerPolicyJson = "{\"timerMode\":\"SECTION_SHARED\",\"warningThresholdSeconds\":300}",
                LayoutType = "SPLIT_HORIZONTAL",
                OptionCount = 4,
                ClusterSize = 1,
                UnitCount = 1,
                HideOptionText = false,
                RequirePassage = true,
                RequireAudio = false,
                RequireImage = false,
                RequiredKeywordCount = 0,
                AllowedComponentTypesJson = "[\"free_response\"]",
                ToolsConfigJson = "{\"allowHighlight\":true,\"allowNotes\":true,\"showWordCount\":true}"
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var loaded = await db.Sections.SingleAsync(s => s.SectionId == section.SectionId);
            Assert.Equal(2.0m, loaded.ScoreWeight);
            Assert.Equal("FULL_SANDBOX", loaded.NavigationMode);
            Assert.Equal("SPLIT_HORIZONTAL", loaded.LayoutType);
            Assert.Equal(4, loaded.OptionCount);
            Assert.True(loaded.RequirePassage);
            Assert.NotNull(loaded.TimerPolicyJson);
            Assert.NotNull(loaded.ToolsConfigJson);

            // Rejects invalid NavigationMode
            await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await RunSqlAsync(connectionString, $"""
                    INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, NavigationMode)
                    VALUES ({skill.SkillId}, {cert.CertId}, 'writing', 'BAD_NAV', N'Bad Nav', 'INVALID_NAV');
                    """);
            });
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task MultiSelect_Allows_Multiple_Correct_Options_While_MCQ_Enforces_OneCorrect()
    {
        var databaseName = $"MultiSelect_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var cert = new Certificate { Code = "MS_CERT", Name = "Cert" };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Part 5" };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam { CertId = cert.CertId, Name = "Exam", DurationMinutes = 60, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var group = new QuestionGroup { CertId = cert.CertId, ExamId = exam.ExamId, SectionId = section.SectionId, UnitNumber = 2 };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            // 1. multi_select question with 2 correct options must SUCCEED
            var msQuestion = new Question
            {
                GroupId = group.GroupId,
                ExamId = exam.ExamId,
                SectionId = section.SectionId,
                SkillId = skill.SkillId,
                Stem = "Choose two correct answers",
                DifficultyLevel = 2,
                QuestionType = "multi_select",
                MaxSelections = 2
            };
            db.Questions.Add(msQuestion);
            await db.SaveChangesAsync();

            var opt1 = new QuestionOption { QuestionId = msQuestion.QuestionId, QuestionType = "multi_select", Label = "A", Text = "Correct 1", IsCorrect = true };
            var opt2 = new QuestionOption { QuestionId = msQuestion.QuestionId, QuestionType = "multi_select", Label = "B", Text = "Correct 2", IsCorrect = true };
            var opt3 = new QuestionOption { QuestionId = msQuestion.QuestionId, QuestionType = "multi_select", Label = "C", Text = "Incorrect", IsCorrect = false };
            db.QuestionOptions.AddRange(opt1, opt2, opt3);
            await db.SaveChangesAsync();

            var correctCount = await db.QuestionOptions.CountAsync(o => o.QuestionId == msQuestion.QuestionId && o.IsCorrect);
            Assert.Equal(2, correctCount);

            // 2. mcq question with 2 correct options must FAIL UX_Options_OneCorrect
            var mcqQuestion = new Question
            {
                GroupId = group.GroupId,
                ExamId = exam.ExamId,
                SectionId = section.SectionId,
                SkillId = skill.SkillId,
                Stem = "Single choice question",
                DifficultyLevel = 1,
                QuestionType = "mcq"
            };
            db.Questions.Add(mcqQuestion);
            await db.SaveChangesAsync();

            var mcqOpt1 = new QuestionOption { QuestionId = mcqQuestion.QuestionId, QuestionType = "mcq", Label = "A", Text = "Correct 1", IsCorrect = true };
            db.QuestionOptions.Add(mcqOpt1);
            await db.SaveChangesAsync();

            var mcqOpt2 = new QuestionOption { QuestionId = mcqQuestion.QuestionId, QuestionType = "mcq", Label = "B", Text = "Correct 2 (Should Fail)", IsCorrect = true };
            db.QuestionOptions.Add(mcqOpt2);
            await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task InlineCompletion_Supports_MaxWords_And_AcceptedAnswersJson()
    {
        var databaseName = $"InlineComp_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var cert = new Certificate { Code = "IC_CERT", Name = "Cert" };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "LIST", Name = "Listening", Modality = "listening" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "listening", Code = "P1", Name = "Part 1" };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam { CertId = cert.CertId, Name = "Exam", DurationMinutes = 30, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var group = new QuestionGroup { CertId = cert.CertId, ExamId = exam.ExamId, SectionId = section.SectionId, UnitNumber = 1 };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            // inline_completion specifies MaxWords, AllowNumber, AcceptedAnswersJson
            var q = new Question
            {
                GroupId = group.GroupId,
                ExamId = exam.ExamId,
                SectionId = section.SectionId,
                SkillId = skill.SkillId,
                Stem = "The library is closed on [blank] afternoons.",
                DifficultyLevel = 2,
                QuestionType = "inline_completion",
                MaxWords = 2,
                AllowNumber = true,
                AcceptedAnswersJson = "[\"wednesday\",\"wed\"]"
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();

            var loaded = await db.Questions.SingleAsync(x => x.QuestionId == q.QuestionId);
            Assert.Equal("inline_completion", loaded.QuestionType);
            Assert.Equal(2, loaded.MaxWords);
            Assert.True(loaded.AllowNumber);
            Assert.Equal("[\"wednesday\",\"wed\"]", loaded.AcceptedAnswersJson);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Authoritative_Sitting_State_And_MultiComponent_Answers_Persist()
    {
        var databaseName = $"SittingState_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);

            var cert = new Certificate { Code = "SIT_CERT", Name = "Cert" };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var role = new Role { Code = "learner_sit", Name = "Learner" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var user = new User
            {
                RoleId = role.RoleId,
                Email = "candidate@test.local",
                PasswordHash = [0x01, 0x02]
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "SEC", Name = "Section" };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam { CertId = cert.CertId, Name = "Sitting Exam", DurationMinutes = 60, Purpose = "mock", Status = "published" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var group = new QuestionGroup { CertId = cert.CertId, ExamId = exam.ExamId, SectionId = section.SectionId, UnitNumber = 1 };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            var q = new Question
            {
                GroupId = group.GroupId,
                ExamId = exam.ExamId,
                SectionId = section.SectionId,
                SkillId = skill.SkillId,
                Stem = "Fill in the blank",
                DifficultyLevel = 1,
                QuestionType = "inline_completion"
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();

            var deadline = DateTime.UtcNow.AddMinutes(45);
            var attempt = new ExamAttempt
            {
                UserId = user.UserId,
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                ActiveSkillId = skill.SkillId,
                ActiveUnitNumber = 2,
                ActiveGroupId = group.GroupId,
                AuthorizationCode = "AUTH-9876543210-CRASH-REC",
                PhaseDeadlineAt = deadline,
                LastAudioOffsetMs = 15400,
                CandidateNotesJson = "[{\"id\":\"n1\",\"text\":\"highlight\"}]",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Status = "in_progress"
            };
            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var loadedAttempt = await db.ExamAttempts
                .Include(a => a.ActiveSkill)
                .Include(a => a.ActiveGroup)
                .SingleAsync(a => a.AttemptId == attempt.AttemptId);

            Assert.Equal(skill.SkillId, loadedAttempt.ActiveSkillId);
            Assert.Equal("READ", loadedAttempt.ActiveSkill!.Code);
            Assert.Equal(2, loadedAttempt.ActiveUnitNumber);
            Assert.Equal(group.GroupId, loadedAttempt.ActiveGroupId);
            Assert.Equal("AUTH-9876543210-CRASH-REC", loadedAttempt.AuthorizationCode);
            Assert.Equal(15400, loadedAttempt.LastAudioOffsetMs);
            Assert.NotNull(loadedAttempt.CandidateNotesJson);

            // AttemptAnswers multi-component responses
            var answer = new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                QuestionId = q.QuestionId,
                ExamId = exam.ExamId,
                ResponseText = "railway station",
                SelectedOptionIdsJson = "[101, 102]",
                IsCorrect = true
            };
            db.AttemptAnswers.Add(answer);
            await db.SaveChangesAsync();

            var loadedAnswer = await db.AttemptAnswers.SingleAsync(a => a.AnswerId == answer.AnswerId);
            Assert.Equal("railway station", loadedAnswer.ResponseText);
            Assert.Equal("[101, 102]", loadedAnswer.SelectedOptionIdsJson);
            Assert.True(loadedAnswer.IsCorrect);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
