using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
public sealed class GenerativeExamServiceTests(DatabaseFixture database)
{
    private sealed class TestClaudeClient : IClaudeClient
    {
        public Func<string, string?, string>? Responder { get; set; }
        public string? LastUserPrompt { get; private set; }
        public string? LastSystemPrompt { get; private set; }

        public Task<string> CreateMessageAsync(
            string userPrompt,
            string? systemPrompt = null,
            int maxTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            LastUserPrompt = userPrompt;
            LastSystemPrompt = systemPrompt;
            if (Responder is not null)
            {
                return Task.FromResult(Responder(userPrompt, systemPrompt));
            }
            return Task.FromResult("{}");
        }
    }

    private sealed record TestEnvironment(
        int CertId,
        string CertCode,
        int AdminId,
        int OtherCertId,
        string OtherCertCode);

    private async Task<TestEnvironment> SeedEnvironmentAsync()
    {
        var certCode = $"TOEIC_{Guid.NewGuid():N}"[..15];
        var otherCertCode = $"IELTS_{Guid.NewGuid():N}"[..15];
        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{adminEmail}', 0x01, N'Exam Admin', SYSDATETIMEOFFSET());

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Generative TOEIC Cert', 1);
            DECLARE @c1 INT = SCOPE_IDENTITY();

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{otherCertCode}', N'Other Cert', 1);
            DECLARE @c2 INT = SCOPE_IDENTITY();

            -- c1 (TOEIC): READ -> P5, P6
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@c1, 'READ', N'Reading', 'reading', 1);
            DECLARE @s1 INT = (SELECT SkillId FROM Skills WHERE CertId = @c1 AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, DisplayOrder) VALUES
              (@s1, @c1, 'reading', 'P5', N'Incomplete Sentences', 4, 1),
              (@s1, @c1, 'reading', 'P6', N'Text Completion', 4, 2);

            INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
              (@c1, 'grammar_prep', N'Prepositions'),
              (@c1, 'reading_vocab', N'Vocabulary');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@c1, 'B1', N'Intermediate', 0, 549, 1),
              (@c1, 'B2', N'Upper Intermediate', 550, 784, 2),
              (@c1, 'C1', N'Advanced', 785, 990, 3);

            -- c2 (Other): WRIT -> T1
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@c2, 'WRIT', N'Writing', 'writing', 1);
            DECLARE @s2 INT = (SELECT SkillId FROM Skills WHERE CertId = @c2 AND Code = 'WRIT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, DisplayOrder) VALUES
              (@s2, @c2, 'writing', 'T1', N'Task 1 Writing', NULL, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var c1 = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var c2 = await db.Certificates.Where(c => c.Code == otherCertCode).Select(c => c.CertId).SingleAsync();
        var admin = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.UserId).SingleAsync();

        return new TestEnvironment(c1, certCode, admin, c2, otherCertCode);
    }

    private static string SampleValidManifest(string certTitle, string sectionCode = "P5", string categoryCode = "grammar_prep") => $$"""
        {
          "version": "1.0",
          "exam": {
            "title": "{{certTitle}}",
            "purpose": "mock",
            "durationMinutes": 60,
            "sourceName": "Claude Sonnet 3.7 Ingest",
            "description": "Auto-generated test package."
          },
          "sections": [
            {
              "sectionCode": "{{sectionCode}}",
              "groups": [
                {
                  "displayOrder": 1,
                  "questions": [
                    {
                      "displayOrder": 1,
                      "stem": "The project proposal must be submitted _____ Friday afternoon.",
                      "difficultyLevel": 3,
                      "questionType": "mcq",
                      "categoryCode": "{{categoryCode}}",
                      "explanation": "'By' indicates a deadline or point before which something must happen.",
                      "hint": "Identify the preposition used for deadlines.",
                      "options": [
                        { "label": "A", "text": "by", "isCorrect": true, "rationale": "Correct preposition for deadlines." },
                        { "label": "B", "text": "at", "isCorrect": false, "rationale": "Incorrect for days of the week." },
                        { "label": "C", "text": "on", "isCorrect": false, "rationale": "Means exactly on Friday, not a deadline." },
                        { "label": "D", "text": "to", "isCorrect": false, "rationale": "Directional preposition." }
                      ]
                    },
                    {
                      "displayOrder": 2,
                      "stem": "All employees should verify their contact details _____ annual HR records.",
                      "difficultyLevel": 3,
                      "questionType": "mcq",
                      "categoryCode": "{{categoryCode}}",
                      "explanation": "'For' indicates the purpose or beneficiary.",
                      "hint": "Express purpose.",
                      "options": [
                        { "label": "A", "text": "for", "isCorrect": true, "rationale": "Expresses purpose correctly." },
                        { "label": "B", "text": "during", "isCorrect": false, "rationale": "Indicates duration of time." },
                        { "label": "C", "text": "between", "isCorrect": false, "rationale": "Requires two items." },
                        { "label": "D", "text": "among", "isCorrect": false, "rationale": "Requires a group of three or more." }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task GenerateAndIngestExamAsync_WithValidBlueprint_CreatesExamInDatabase()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();
        claude.Responder = (user, sys) => $"```json\n{SampleValidManifest("Generative Reading Form 1")}\n```";

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        var blueprint = new ExamBlueprintDto(
            CertCode: env.CertCode,
            Title: "Generative Reading Form 1",
            Purpose: "mock",
            Status: "draft",
            TargetBand: "B2",
            DifficultyLevel: 3,
            Sections:
            [
                new ExamBlueprintSectionDto(SectionCode: "P5", QuestionCount: 2, CategoryCode: "grammar_prep")
            ]);

        var result = await generator.GenerateAndIngestExamAsync(blueprint, env.AdminId);

        Assert.NotNull(result);
        Assert.True(result.ExamId > 0);
        Assert.Equal(env.CertId, result.CertId);
        Assert.Equal("draft", result.Status);
        Assert.Equal(2, result.QuestionCount);

        var savedExam = await db.Exams
            .SingleAsync(e => e.ExamId == result.ExamId);

        var savedQuestions = await db.Questions
            .Where(q => q.ExamId == result.ExamId)
            .ToListAsync();

        Assert.Equal("Generative Reading Form 1", savedExam.Name);
        Assert.Equal("draft", savedExam.Status);
        Assert.Equal(2, savedQuestions.Count);

        foreach (var q in savedQuestions)
        {
            var options = await db.QuestionOptions.Where(o => o.QuestionId == q.QuestionId).ToListAsync();
            Assert.Equal(4, options.Count);
            Assert.Single(options, o => o.IsCorrect);
        }
    }

    [Fact]
    public async Task GenerateAndIngestExamAsync_WithPublishedStatus_SetsPublishedStatus()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();
        claude.Responder = (user, sys) => SampleValidManifest("Published Form A");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        var blueprint = new ExamBlueprintDto(
            CertCode: env.CertCode,
            Title: "Published Form A",
            Purpose: "mock",
            Status: "published",
            Sections: [new ExamBlueprintSectionDto("P5", 2)]);

        var result = await generator.GenerateAndIngestExamAsync(blueprint, env.AdminId);

        Assert.Equal("published", result.Status);
        var savedExam = await db.Exams.SingleAsync(e => e.ExamId == result.ExamId);
        Assert.Equal("published", savedExam.Status);
    }

    [Fact]
    public async Task GenerateAndIngestExamAsync_WithUnknownCertCode_ThrowsNotFound()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        var blueprint = new ExamBlueprintDto(
            CertCode: "NONEXISTENT",
            Sections: [new ExamBlueprintSectionDto("P5", 2)]);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            generator.GenerateAndIngestExamAsync(blueprint, env.AdminId));

        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task GenerateAndIngestExamAsync_WithCrossCertificateSection_ThrowsValidationAndPreventsLeakage()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        // Blueprint asks for section 'T1' which belongs to OtherCert (IELTS), NOT TOEIC
        var blueprint = new ExamBlueprintDto(
            CertCode: env.CertCode,
            Sections: [new ExamBlueprintSectionDto("T1", 1)]);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            generator.GenerateAndIngestExamAsync(blueprint, env.AdminId));

        Assert.Contains("Cross-certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAndIngestExamAsync_WhenClaudeReturnsInvalidMcq_FailsAndRollsBack()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();
        // Return MCQ with TWO correct options (violating UX_Options_OneCorrect)
        claude.Responder = (user, sys) => """
            {
              "version": "1.0",
              "exam": { "title": "Invalid Form", "purpose": "mock", "durationMinutes": 60 },
              "sections": [
                {
                  "sectionCode": "P5",
                  "groups": [
                    {
                      "displayOrder": 1,
                      "questions": [
                        {
                          "displayOrder": 1,
                          "stem": "Invalid question with two correct answers",
                          "difficultyLevel": 3,
                          "questionType": "mcq",
                          "options": [
                            { "label": "A", "text": "opt 1", "isCorrect": true, "rationale": "corr 1" },
                            { "label": "B", "text": "opt 2", "isCorrect": true, "rationale": "corr 2" }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        var blueprint = new ExamBlueprintDto(
            CertCode: env.CertCode,
            Sections: [new ExamBlueprintSectionDto("P5", 1)]);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            generator.GenerateAndIngestExamAsync(blueprint, env.AdminId));

        Assert.Equal(400, ex.Status);

        // Verify rollback: no exam with title "Invalid Form" was persisted
        var exists = await db.Exams.AnyAsync(e => e.Name == "Invalid Form");
        Assert.False(exists);
    }

    [Fact]
    public async Task GeneratedExam_CanBeSatViaAttemptEngine_SuccessfullyScoresAndCompletes()
    {
        var env = await SeedEnvironmentAsync();
        var claude = new TestClaudeClient();
        claude.Responder = (user, sys) => SampleValidManifest("Sat Engine Form");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var importService = new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System);
        var generator = new ExamGeneratorService(db, importService, claude);

        var blueprint = new ExamBlueprintDto(
            CertCode: env.CertCode,
            Title: "Sat Engine Form",
            Purpose: "mock",
            Status: "published",
            Sections: [new ExamBlueprintSectionDto("P5", 2)]);

        var genResult = await generator.GenerateAndIngestExamAsync(blueprint, env.AdminId);

        // Arrange a learner
        var learnerEmail = $"learner-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{learnerEmail}', 0x01, N'Engine Learner', {env.CertId});
            """);

        var learnerId = await db.Users.Where(u => u.Email == learnerEmail).Select(u => u.UserId).SingleAsync();

        // Sitting mechanics via IAttemptEngine
        var engine = new AttemptEngine(db);
        var attempt = await engine.CreateAttemptAsync(learnerId, env.CertId, genResult.ExamId, 60);

        Assert.NotNull(attempt);
        Assert.Equal(AttemptStatus.InProgress.ToCode(), attempt.Status);

        // Fetch questions and answer them correctly
        var questions = await db.Questions
            .Where(q => q.ExamId == genResult.ExamId)
            .ToListAsync();

        Assert.Equal(2, questions.Count);

        foreach (var q in questions)
        {
            var options = await db.QuestionOptions.Where(o => o.QuestionId == q.QuestionId).ToListAsync();
            var correctOpt = options.Single(o => o.IsCorrect);
            await engine.SaveAnswerAsync(attempt.AttemptId, learnerId, q.QuestionId, correctOpt.OptionId, isFlagged: false);
        }

        var grading = await engine.SubmitAttemptAsync(attempt.AttemptId, learnerId, isAutoSubmit: false);

        Assert.NotNull(grading);
        Assert.Equal(AttemptStatus.Graded.ToCode(), grading.Attempt.Status);
        Assert.Equal(2, grading.Answers.Count(a => a.IsCorrect == true));
    }
}
