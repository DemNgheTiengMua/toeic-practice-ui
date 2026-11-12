using System.Text.Json;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BusinessObjects;

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

                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
                  (@skill1, @certHalf, 'listening', 'SEC1', N'Section 1', 1, 1),
                  (@skill2, @certHalf, 'reading', 'SEC2', N'Section 2', 1, 2);

                INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
                VALUES (@certHalf, N'Exam 1', 'published', 60, 'mock');
                DECLARE @examId INT = SCOPE_IDENTITY();

                -- Attempt with scaled scores: 60 and 65 -> Average = 62.5 -> IELTS half-step rounding = 65
                INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                VALUES (@userId, @examId, @certHalf, 'in_progress', DATEADD(hour, 1, SYSDATETIMEOFFSET()), 65, 'AttemptEngine', 1);
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

                    INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, ExpiresAt, TotalScore, WrittenByService, ProjectionVersion)
                    VALUES (@userId, @examId, @certHalf, 'in_progress', DATEADD(hour, 1, SYSDATETIMEOFFSET()), 125, 'AttemptEngine', 1);
                    DECLARE @badAttempt INT = SCOPE_IDENTITY();

                    INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
                    VALUES (@badAttempt, @skill1, @certHalf, 10, 60);
                    """);
            });
            Assert.Equal(50011, ex.Number);

            // #171: `independent` was removed from CK_Certificates_ScoreAggregationRule. It returned
            // a total of 0 for every attempt and the trigger that checks a total against its skills
            // exempted it, so a certificate could be created whose learners could never score.
            // The value is now unstorable, which is what makes that state unrepresentable.
            var independent = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await RunSqlAsync(connectionString, """
                    INSERT INTO Certificates (Code, Name, ScoreAggregationRule)
                    VALUES ('INDEP_CERT', N'Independent Cert', 'independent');
                    """);
            });
            Assert.Contains("CK_Certificates_ScoreAggregationRule", independent.Message, StringComparison.Ordinal);
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

            var section = new Section {
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
                ExpectedQuestionCount = 1,
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
                    INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, NavigationMode)
                    VALUES ({skill.SkillId}, {cert.CertId}, 'writing', 'BAD_NAV', N'Bad Nav', 1, 'INVALID_NAV');
                    """);
            });
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }


    [Fact]
    public async Task InlineCompletion_Supports_MaxWords_And_AcceptedAnswersJson()
    {
        // #272: migrated. The blank's answer shape is the pool's: MaxWords, AllowNumber and
        // AcceptedAnswersJson all live on ItemVersions, which is where a blank's key belongs (its answer
        // is a set of strings, not a label). The assertions are unchanged; only the store they read is.
        var databaseName = $"InlineComp_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);

            // A fresh test database is empty and Items needs a topic, so the vocabulary row is authored
            // here rather than assumed: 'L-DET' is one of the closed twenty.
            var topic = new Topic { Modality = "listening", Code = "L-DET", Name = "Listening for detail" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();

            // A blank renders as inline_completion and carries no options at all - the "no options"
            // branch of CK_ItemVersions_OptionsComplete, whose key is AcceptedAnswersJson rather than a
            // label.
            var item = new Item
            {
                Modality = "listening",
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "short_exchange",
                RenderFormat = "inline_completion",
                ContentTier = "demo",
                LifecycleStatus = "active"
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = "The library is closed on [blank] afternoons.",
                // #272: the pool refuses a ceiling with no floor (CK_ItemVersions_WordRange), where the
                // legacy Questions table allowed MaxWords alone. The subject of this test - a two-word
                // ceiling and an accepted-answer set - is unchanged; the floor it now needs is 1.
                MinWords = 1,
                MaxWords = 2,
                AllowNumber = true,
                AcceptedAnswersJson = "[\"wednesday\",\"wed\"]"
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            var loaded = await db.ItemVersions.SingleAsync(x => x.ItemVersionId == version.ItemVersionId);
            Assert.Equal("inline_completion", (await db.Items.SingleAsync(i => i.ItemId == loaded.ItemId)).RenderFormat);
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

            var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "SEC", Name = "Section", ExpectedQuestionCount = 1 };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam { CertId = cert.CertId, Name = "Sitting Exam", DurationMinutes = 60, Purpose = "mock", Status = "published" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            // #272: migrated to the pool. The position is an ExamItems binding and the block address it
            // implies is the solo-position convention - the negated ExamItemId - which is what a
            // HYBRID_GROUP_LOCKED sitting stores when its unit shares no stimulus.
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Fill in the blank",
                displayOrder: 1, keyLabel: "A", level: "A2");
            var blockAddress = -position.ExamItemId;

            var deadline = DateTime.UtcNow.AddMinutes(45);
            var attempt = new ExamAttempt
            {
                UserId = user.UserId,
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                ActiveSkillId = skill.SkillId,
                ActiveUnitNumber = 2,
                ActiveGroupId = blockAddress,
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
                .SingleAsync(a => a.AttemptId == attempt.AttemptId);

            Assert.Equal(skill.SkillId, loadedAttempt.ActiveSkillId);
            Assert.Equal("READ", loadedAttempt.ActiveSkill!.Code);
            Assert.Equal(2, loadedAttempt.ActiveUnitNumber);
            // #269: the block address round-trips as a value. It is no longer a navigation, because a
            // solo position's address is its own negated ExamItemId and names no row.
            Assert.Equal(blockAddress, loadedAttempt.ActiveGroupId);
            Assert.Equal("AUTH-9876543210-CRASH-REC", loadedAttempt.AuthorizationCode);
            Assert.Equal(15400, loadedAttempt.LastAudioOffsetMs);
            Assert.NotNull(loadedAttempt.CandidateNotesJson);

            // AttemptAnswers multi-component responses. #272: the row names its roster position, and
            // pins the version so re-versioning the item cannot change what this answer means.
            var answer = new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamItemId = position.ExamItemId,
                ExamId = exam.ExamId,
                ItemId = position.ItemId,
                ItemVersionId = position.ItemVersionId,
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
            Assert.Equal(position.ExamItemId, loadedAnswer.ExamItemId);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
