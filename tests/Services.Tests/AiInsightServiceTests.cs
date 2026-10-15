using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AiInsightServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (AiInsightService service, FakeAiInsightGenerator generator) CreateService(FakeAiInsightGenerator? customGenerator = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var generator = customGenerator ?? new FakeAiInsightGenerator();
        var service = new AiInsightService(db, generator, NullLogger<AiInsightService>.Instance);
        return (service, generator);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<(int certId, int sectionId, int sessionId)> SeedPracticeSessionFixtureAsync(int userId, string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4 };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = section.SectionId, DisplayOrder = 1 };
        db.QuestionGroups.Add(grp);
        await db.SaveChangesAsync();

        var category = new QuestionCategory { CertId = cert.CertId, Code = "PREP", Name = "Prepositions" };
        db.QuestionCategories.Add(category);
        await db.SaveChangesAsync();

        var q1 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId, SkillId = skill.SkillId, CategoryId = category.CategoryId, Stem = "Fill preposition: We arrived ___ noon.", DifficultyLevel = 3, QuestionType = "mcq", DisplayOrder = 1 };
        var q2 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId, SkillId = skill.SkillId, CategoryId = category.CategoryId, Stem = "Fill preposition: She sat ___ the table.", DifficultyLevel = 3, QuestionType = "mcq", DisplayOrder = 2 };
        db.Questions.AddRange(q1, q2);
        await db.SaveChangesAsync();

        var o1A = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "A", Text = "at", IsCorrect = true };
        var o1B = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "B", Text = "on", IsCorrect = false };
        var o2A = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "A", Text = "at", IsCorrect = true };
        var o2B = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "B", Text = "to", IsCorrect = false };
        db.QuestionOptions.AddRange(o1A, o1B, o2A, o2B);
        await db.SaveChangesAsync();

        var session = new PracticeSession
        {
            UserId = userId,
            SectionId = section.SectionId,
            Modality = "reading",
            QuestionCount = 2,
            Status = PracticeStatus.Finished.ToCode(),
            CorrectCount = 2,
            QuestionSource = QuestionSource.All.ToCode(),
            FeedbackMode = FeedbackMode.End.ToCode(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        var a1 = new PracticeAnswer
        {
            SessionId = session.SessionId,
            SectionId = section.SectionId,
            QuestionId = q1.QuestionId,
            SelectedOptionId = o1A.OptionId,
            IsCorrect = true,
            HintUsed = false,
            DisplayOrder = 1
        };
        var a2 = new PracticeAnswer
        {
            SessionId = session.SessionId,
            SectionId = section.SectionId,
            QuestionId = q2.QuestionId,
            SelectedOptionId = o2A.OptionId,
            IsCorrect = true,
            HintUsed = true,
            DisplayOrder = 2
        };
        db.PracticeAnswers.AddRange(a1, a2);
        await db.SaveChangesAsync();

        return (cert.CertId, section.SectionId, session.SessionId);
    }

    private async Task<(int certId, int examId, int attemptId)> SeedExamAttemptFixtureAsync(int userId, string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4 };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var band = new ScoreBand { CertId = cert.CertId, Code = "B2", Name = "Vantage", MinTotal = 500, MaxTotal = 990, DisplayOrder = 1 };
        db.ScoreBands.Add(band);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = section.SectionId, DisplayOrder = 1 };
        db.QuestionGroups.Add(grp);
        await db.SaveChangesAsync();

        var q = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId, SkillId = skill.SkillId, Stem = "Exam question stem", DifficultyLevel = 3, QuestionType = "mcq", DisplayOrder = 1 };
        db.Questions.Add(q);
        await db.SaveChangesAsync();

        var opt = new QuestionOption { QuestionId = q.QuestionId, QuestionType = "mcq", Label = "A", Text = "Option A", IsCorrect = true };
        db.QuestionOptions.Add(opt);
        await db.SaveChangesAsync();

        var attempt = new ExamAttempt
        {
            UserId = userId,
            ExamId = exam.ExamId,
            CertId = cert.CertId,
            Status = AttemptStatus.Graded.ToCode(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            SubmittedAt = DateTimeOffset.UtcNow,
            TotalScore = 750,
            BandCode = "B2"
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var ans = new AttemptAnswer
        {
            AttemptId = attempt.AttemptId,
            ExamId = exam.ExamId,
            QuestionId = q.QuestionId,
            SelectedOptionId = opt.OptionId,
            IsCorrect = true
        };
        db.AttemptAnswers.Add(ans);
        await db.SaveChangesAsync();

        return (cert.CertId, exam.ExamId, attempt.AttemptId);
    }

    [Fact]
    public async Task GetOrCreatePracticeInsightAsync_creates_pending_insight_and_reuses_on_reload()
    {
        var certCode = Unique("PR");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        // 1. Initial request -> creates pending insight
        var initial = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);
        Assert.Equal(AiInsightStatus.Pending, initial.Status);
        Assert.Equal(AiInsightScope.PracticeSession, initial.Scope);
        Assert.Equal(sessionId, initial.PracticeSessionId);
        Assert.NotNull(initial.SourceFingerprint);
        Assert.Equal(64, initial.SourceFingerprint.Length);

        // 2. Reloading reuses the stored insight (fingerprint-checked)
        var reloaded = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);
        Assert.Equal(initial.InsightId, reloaded.InsightId);
        Assert.Equal(initial.SourceFingerprint, reloaded.SourceFingerprint);
    }

    [Fact]
    public async Task GetOrCreateExamInsightAsync_creates_pending_insight_and_reuses_on_reload()
    {
        var certCode = Unique("EX");
        var userId = await SeedLearnerAsync();
        var (_, _, attemptId) = await SeedExamAttemptFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        var initial = await service.GetOrCreateExamInsightAsync(userId, attemptId);
        Assert.Equal(AiInsightStatus.Pending, initial.Status);
        Assert.Equal(AiInsightScope.Attempt, initial.Scope);
        Assert.Equal(attemptId, initial.AttemptId);
        Assert.NotNull(initial.SourceFingerprint);
        Assert.Equal(64, initial.SourceFingerprint.Length);

        var reloaded = await service.GetOrCreateExamInsightAsync(userId, attemptId);
        Assert.Equal(initial.InsightId, reloaded.InsightId);
        Assert.Equal(initial.SourceFingerprint, reloaded.SourceFingerprint);
    }

    [Fact]
    public async Task ProcessPendingInsightsAsync_generates_insight_and_links_recommendations()
    {
        var certCode = Unique("PR");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        var pending = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);
        Assert.Equal(AiInsightStatus.Pending, pending.Status);

        // Process queue
        var processed = await service.ProcessPendingInsightsAsync();
        Assert.True(processed >= 1);

        // Verify insight is now done with narrative, trend, and model
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var doneInsight = await db.AiInsights
            .Include(i => i.Recommendations)
            .SingleAsync(i => i.InsightId == pending.InsightId);

        Assert.Equal(AiInsightStatus.Done.ToCode(), doneInsight.Status);
        Assert.NotNull(doneInsight.Narrative);
        Assert.Contains("Performance Summary", doneInsight.Narrative);
        Assert.Contains("Competence vs. Luck", doneInsight.Narrative);
        Assert.Equal(FakeAiInsightGenerator.DefaultModel, doneInsight.Model);
        Assert.NotNull(doneInsight.CompletedAt);

        // Invariant: Recommendations linked to InsightId (Recommendations.InsightId = insight.InsightId)
        Assert.NotEmpty(doneInsight.Recommendations);
        foreach (var reco in doneInsight.Recommendations)
        {
            Assert.Equal(doneInsight.InsightId, reco.InsightId);
            Assert.Null(reco.GradingId); // Invariant CK_Reco_OneProducer
            Assert.Equal(sessionId, reco.PracticeSessionId); // Invariant CK_Reco_OneSource
            Assert.Null(reco.AttemptId);
            Assert.Equal(userId, reco.UserId);
        }
    }

    [Fact]
    public async Task Simulated_failure_and_retry_flow_works_correctly()
    {
        var certCode = Unique("FL");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var generator = new FakeAiInsightGenerator { ShouldSimulateFailure = true };
        var (service, _) = CreateService(generator);

        var pending = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);

        // First run: fails due to simulated failure
        var processed = await service.ProcessPendingInsightsAsync();
        Assert.True(processed >= 1);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var failed = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Failed.ToCode(), failed!.Status);
            Assert.NotNull(failed.FailureReason);
        }

        // Retry failed insight
        await service.RetryPracticeInsightAsync(userId, sessionId);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var retried = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Pending.ToCode(), retried!.Status);
            Assert.Null(retried.FailureReason);
        }

        // Process again: retry (AttemptCount = 2) clears failure
        var processedRetry = await service.ProcessPendingInsightsAsync();
        Assert.True(processedRetry >= 1);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var completed = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Done.ToCode(), completed!.Status);
            Assert.NotNull(completed.Narrative);
        }
    }

    [Fact]
    public async Task Retry_on_non_failed_insight_throws_exception()
    {
        var certCode = Unique("NF");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();
        await service.GetOrCreatePracticeInsightAsync(userId, sessionId);

        // Still pending (not failed)
        var ex = await Assert.ThrowsAsync<AiInsightException>(() => service.RetryPracticeInsightAsync(userId, sessionId));
        Assert.Equal(ApiError.Codes.InsightNotFailed, ex.Code);
    }

    [Fact]
    public async Task GetOrCreatePracticeInsightAsync_handles_concurrent_creation_without_throwing_duplicate_key_error()
    {
        var certCode = Unique("CC");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        // Simulate two concurrent requests arriving with separate DbContext instances
        var (service1, _) = CreateService();
        var (service2, _) = CreateService();

        var task1 = service1.GetOrCreatePracticeInsightAsync(userId, sessionId);
        var task2 = service2.GetOrCreatePracticeInsightAsync(userId, sessionId);

        var results = await Task.WhenAll(task1, task2);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.Equal(results[0].InsightId, results[1].InsightId);
        Assert.Equal(results[0].SourceFingerprint, results[1].SourceFingerprint);
    }

    [Fact]
    public async Task GetOrCreateExamInsightAsync_throws_AttemptNotFinished_if_attempt_is_not_graded()
    {
        var certCode = Unique("NG");
        var userId = await SeedLearnerAsync();
        var (_, _, attemptId) = await SeedExamAttemptFixtureAsync(userId, certCode);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var attempt = await db.ExamAttempts.FindAsync(attemptId);
            attempt!.Status = AttemptStatus.InProgress.ToCode();
            attempt.SubmittedAt = null;
            await db.SaveChangesAsync();
        }

        var (service, _) = CreateService();
        var ex = await Assert.ThrowsAsync<AiInsightException>(() => service.GetOrCreateExamInsightAsync(userId, attemptId));
        Assert.Equal(ApiError.Codes.AttemptNotFinished, ex.Code);
    }

    [Fact]
    public async Task ProcessPendingInsightsAsync_sanitizes_invalid_foreign_keys_in_recommendations()
    {
        var certCode = Unique("FK");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        // Generator returning recommendation with nonexistent band code and nonexistent skill
        var mockGenerator = new CustomRecoGenerator(new[]
        {
            new GeneratedRecommendation("Tip with invalid FKs", RecommendationSeverity.Info, SkillId: 999999, TargetBandCode: "NONEXISTENT_BAND")
        });

        var (service, _) = CreateService(mockGenerator);
        var pending = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);

        var processed = await service.ProcessPendingInsightsAsync();
        Assert.True(processed >= 1);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reco = await db.Recommendations.SingleAsync(r => r.InsightId == pending.InsightId);
        Assert.Null(reco.TargetBandCode);
        Assert.Null(reco.SkillId);
        Assert.Equal("Tip with invalid FKs", reco.Text);
    }

    [Fact]
    public async Task ProcessPendingInsightsAsync_propagates_cancellation_without_failing_insight()
    {
        var certCode = Unique("CN");
        var userId = await SeedLearnerAsync();
        var (_, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();
        var pending = await service.GetOrCreatePracticeInsightAsync(userId, sessionId);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessPendingInsightsAsync(cts.Token));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var insight = await db.AiInsights.FindAsync(pending.InsightId);
        Assert.Equal(AiInsightStatus.Pending.ToCode(), insight!.Status);
        Assert.Null(insight.FailureReason);
    }

    private sealed class CustomRecoGenerator(IReadOnlyList<GeneratedRecommendation> recommendations) : FakeAiInsightGenerator
    {
        public override Task<AiInsightResult> GenerateInsightAsync(AiInsightGenerationRequest request, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiInsightResult(
                Success: true,
                FailureReason: null,
                Model: DefaultModel,
                Narrative: "Custom narrative for FK testing.",
                Trend: AiInsightTrend.Improving,
                Recommendations: recommendations));
        }
    }
}
