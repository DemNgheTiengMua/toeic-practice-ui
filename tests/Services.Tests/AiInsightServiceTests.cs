using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    public async Task GetOrCreateDashboardInsightAsync_creates_pending_insight_and_reuses_on_reload()
    {
        var certCode = Unique("DB");
        var userId = await SeedLearnerAsync();
        var (certId, _, sessionId) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        // 1. Initial request -> creates pending dashboard insight
        var initial = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.Equal(AiInsightStatus.Pending, initial.Status);
        Assert.Equal(AiInsightScope.Dashboard, initial.Scope);
        Assert.Null(initial.PracticeSessionId);
        Assert.Null(initial.AttemptId);
        Assert.Equal(certId, initial.CertId);
        Assert.NotNull(initial.SourceFingerprint);
        Assert.Equal(64, initial.SourceFingerprint.Length);

        // 2. Reload without activity -> reuses existing insight (UX_AiInsights_Fingerprint)
        var reloaded = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.Equal(initial.InsightId, reloaded.InsightId);
        Assert.Equal(initial.SourceFingerprint, reloaded.SourceFingerprint);
    }

    [Fact]
    public async Task GetOrCreateDashboardInsightAsync_concurrent_first_loads_create_one_row_without_a_failed_save()
    {
        // Several dashboard loads for one learner at once (two tabs, a double-clicked sign-in,
        // parallel E2E fixtures) used to race: every lookup found nothing, every caller inserted,
        // and all but one hit UX_AiInsights_Fingerprint. The catch re-read the winner, but EF had
        // already logged each loser as "fail: Microsoft.EntityFrameworkCore.Update[10000]".
        var userId = await SeedLearnerAsync();
        var (certId, _, _) = await SeedPracticeSessionFixtureAsync(userId, Unique("RC"));

        const int callers = 4;
        var lookups = new FingerprintLookupLatch(callers);
        var failedSaves = new FailedSaveCounter();
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(lookups, failedSaves)
            .Options;

        // Warm the model and query cache first, so the racing calls reach their lookup together.
        var warmUpLearner = await SeedLearnerAsync();
        await using (var warm = new CompanionDbContext(options))
        {
            await new AiInsightService(warm, new FakeAiInsightGenerator(), NullLogger<AiInsightService>.Instance)
                .GetOrCreateDashboardInsightAsync(warmUpLearner, certId);
        }

        lookups.Arm();
        var contexts = Enumerable.Range(0, callers).Select(_ => new CompanionDbContext(options)).ToList();
        try
        {
            var results = await Task.WhenAll(contexts.Select(db => Task.Run(() =>
                new AiInsightService(db, new FakeAiInsightGenerator(), NullLogger<AiInsightService>.Instance)
                    .GetOrCreateDashboardInsightAsync(userId, certId))));

            Assert.Single(results.Select(r => r.InsightId).Distinct());
            Assert.Equal(0, failedSaves.Count);
        }
        finally
        {
            foreach (var db in contexts)
            {
                await db.DisposeAsync();
            }
        }

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(1, await verify.AiInsights.CountAsync(i => i.UserId == userId && i.Scope == AiInsightScope.Dashboard.ToCode()));
    }

    /// <summary>Holds each dashboard fingerprint lookup until every racing caller has made its own
    /// (or a second passes), so unserialised callers all reach the insert having seen no row.</summary>
    private sealed class FingerprintLookupLatch(int callers) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;
        private int arrived;

        public void Arm() => Volatile.Write(ref armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref armed) == 1
                && command.CommandText.Contains("[AiInsights]", StringComparison.Ordinal)
                && command.CommandText.Contains("[SourceFingerprint] =", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrived) >= callers)
                {
                    allArrived.TrySetResult();
                }

                await Task.WhenAny(allArrived.Task, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            }

            return result;
        }
    }

    /// <summary>Counts SaveChanges failures: the event EF logs as Update[10000].</summary>
    private sealed class FailedSaveCounter : SaveChangesInterceptor
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetOrCreateDashboardInsightAsync_recomputes_fingerprint_when_new_activity_occurs()
    {
        var certCode = Unique("FP");
        var userId = await SeedLearnerAsync();
        var (certId, sectionId, sessionId1) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        var initial = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.NotNull(initial.SourceFingerprint);

        // Process initial insight
        await service.ProcessPendingInsightsAsync();

        // Learner completes another practice session -> new activity
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var session2 = new PracticeSession
            {
                UserId = userId,
                SectionId = sectionId,
                Modality = "reading",
                QuestionCount = 1,
                Status = PracticeStatus.Finished.ToCode(),
                CorrectCount = 1,
                QuestionSource = QuestionSource.All.ToCode(),
                FeedbackMode = FeedbackMode.End.ToCode(),
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                FinishedAt = DateTimeOffset.UtcNow
            };
            db.PracticeSessions.Add(session2);
            await db.SaveChangesAsync();

            var q = await db.Questions.FirstAsync(q => q.SectionId == sectionId);
            var opt = await db.QuestionOptions.FirstAsync(o => o.QuestionId == q.QuestionId && o.IsCorrect);
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session2.SessionId,
                SectionId = sectionId,
                QuestionId = q.QuestionId,
                SelectedOptionId = opt.OptionId,
                IsCorrect = true,
                HintUsed = false,
                DisplayOrder = 1
            });
            await db.SaveChangesAsync();
        }

        // Subsequent call -> fingerprint changes because new session and accuracy numbers changed
        var updated = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.NotEqual(initial.InsightId, updated.InsightId);
        Assert.NotEqual(initial.SourceFingerprint, updated.SourceFingerprint);
    }

    [Fact]
    public async Task ProcessPendingInsightsAsync_processes_dashboard_insight_and_links_recommendations()
    {
        var certCode = Unique("DP");
        var userId = await SeedLearnerAsync();
        var (certId, _, _) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        var pending = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.Equal(AiInsightStatus.Pending, pending.Status);

        var processed = await service.ProcessPendingInsightsAsync();
        Assert.True(processed >= 1);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var doneInsight = await db.AiInsights
            .Include(i => i.Recommendations)
            .SingleAsync(i => i.InsightId == pending.InsightId);

        Assert.Equal(AiInsightStatus.Done.ToCode(), doneInsight.Status);
        Assert.NotNull(doneInsight.Narrative);
        Assert.Contains("Performance Summary", doneInsight.Narrative);
        Assert.Contains("Section Breakdown", doneInsight.Narrative);
        Assert.Contains("Target Band Alignment", doneInsight.Narrative);
        Assert.NotNull(doneInsight.Trend);
        Assert.Equal(FakeAiInsightGenerator.DefaultModel, doneInsight.Model);
        Assert.NotNull(doneInsight.CompletedAt);

        // Recommendations must link to InsightId and have null session/attempt IDs (CK_Reco_OneProducer, CK_Reco_OneSource)
        Assert.NotEmpty(doneInsight.Recommendations);
        foreach (var reco in doneInsight.Recommendations)
        {
            Assert.Equal(doneInsight.InsightId, reco.InsightId);
            Assert.Null(reco.GradingId);
            Assert.Null(reco.PracticeSessionId);
            Assert.Null(reco.AttemptId);
            Assert.Equal(userId, reco.UserId);
            Assert.Equal(certId, reco.CertId);
        }
    }

    [Fact]
    public async Task Dashboard_insight_evaluates_trend_and_compares_against_previous_insight()
    {
        var certCode = Unique("TR");
        var userId = await SeedLearnerAsync();
        var (certId, sectionId, _) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var (service, _) = CreateService();

        // 1. Generate first baseline dashboard insight
        var firstPending = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        await service.ProcessPendingInsightsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var firstDone = await db.AiInsights.SingleAsync(i => i.InsightId == firstPending.InsightId);
            Assert.Equal(AiInsightStatus.Done.ToCode(), firstDone.Status);
            Assert.Contains("Initial diagnostic baseline", firstDone.Narrative);
        }

        // 2. Add further practice activity to trigger new insight
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var session2 = new PracticeSession
            {
                UserId = userId,
                SectionId = sectionId,
                Modality = "reading",
                QuestionCount = 1,
                Status = PracticeStatus.Finished.ToCode(),
                CorrectCount = 1,
                QuestionSource = QuestionSource.All.ToCode(),
                FeedbackMode = FeedbackMode.End.ToCode(),
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow
            };
            db.PracticeSessions.Add(session2);
            await db.SaveChangesAsync();

            var q = await db.Questions.FirstAsync(q => q.SectionId == sectionId);
            var opt = await db.QuestionOptions.FirstAsync(o => o.QuestionId == q.QuestionId && o.IsCorrect);
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session2.SessionId,
                SectionId = sectionId,
                QuestionId = q.QuestionId,
                SelectedOptionId = opt.OptionId,
                IsCorrect = true,
                HintUsed = false,
                DisplayOrder = 1
            });
            await db.SaveChangesAsync();
        }

        // 3. Generate second dashboard insight -> should compare against previous insight
        var secondPending = await service.GetOrCreateDashboardInsightAsync(userId, certId);
        Assert.NotEqual(firstPending.InsightId, secondPending.InsightId);

        await service.ProcessPendingInsightsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var secondDone = await db.AiInsights.SingleAsync(i => i.InsightId == secondPending.InsightId);
            Assert.Equal(AiInsightStatus.Done.ToCode(), secondDone.Status);
            Assert.NotNull(secondDone.Trend);
            // Trend is evaluated and narrative compares with previous review
            Assert.Contains("Compared to your previous review", secondDone.Narrative);
        }
    }

    [Fact]
    public async Task RetryDashboardInsightAsync_retries_failed_insight()
    {
        var certCode = Unique("RF");
        var userId = await SeedLearnerAsync();
        var (certId, _, _) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        var generator = new FakeAiInsightGenerator { ShouldSimulateFailure = true };
        var (service, _) = CreateService(generator);

        var pending = await service.GetOrCreateDashboardInsightAsync(userId, certId);

        // Process queue -> fails due to simulated failure
        await service.ProcessPendingInsightsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var failed = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Failed.ToCode(), failed!.Status);
            Assert.NotNull(failed.FailureReason);
        }

        // Retry dashboard insight
        await service.RetryDashboardInsightAsync(userId, certId);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var retried = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Pending.ToCode(), retried!.Status);
            Assert.Null(retried.FailureReason);
        }

        // Process again -> succeeds
        await service.ProcessPendingInsightsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var completed = await db.AiInsights.FindAsync(pending.InsightId);
            Assert.Equal(AiInsightStatus.Done.ToCode(), completed!.Status);
            Assert.NotNull(completed.Narrative);
        }
    }

    [Fact]
    public async Task GetOrCreateDashboardInsightAsync_handles_concurrent_creation_without_throwing_duplicate_key_error()
    {
        var certCode = Unique("DC");
        var userId = await SeedLearnerAsync();
        var (certId, _, _) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        // Simulate two concurrent requests arriving with separate DbContext instances
        var (service1, _) = CreateService();
        var (service2, _) = CreateService();

        var task1 = service1.GetOrCreateDashboardInsightAsync(userId, certId);
        var task2 = service2.GetOrCreateDashboardInsightAsync(userId, certId);

        var results = await Task.WhenAll(task1, task2);

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);
        Assert.Equal(results[0].InsightId, results[1].InsightId);
        Assert.Equal(results[0].SourceFingerprint, results[1].SourceFingerprint);
    }

    [Fact]
    public async Task GetOrCreateDashboardInsightAsync_resolves_active_certificate_when_certId_is_null()
    {
        var certCode = Unique("AC");
        var userId = await SeedLearnerAsync();
        var (certId, _, _) = await SeedPracticeSessionFixtureAsync(userId, certCode);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var user = await db.Users.FindAsync(userId);
            user!.ActiveCertId = certId;
            await db.SaveChangesAsync();
        }

        var (service, _) = CreateService();
        var insight = await service.GetOrCreateDashboardInsightAsync(userId, certId: null);

        Assert.NotNull(insight);
        Assert.Equal(certId, insight.CertId);
        Assert.Equal(AiInsightScope.Dashboard, insight.Scope);
    }

    private async Task<(int certId, int templateId, int pathId, int step1Id, int step2Id)> SeedLearningPathFixtureAsync(int userId, string certCode)
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

        var bandA = new ScoreBand { CertId = cert.CertId, Code = "A2", Name = "Elementary", MinTotal = 200, MaxTotal = 400, DisplayOrder = 1 };
        var bandB = new ScoreBand { CertId = cert.CertId, Code = "B2", Name = "Upper Intermediate", MinTotal = 700, MaxTotal = 900, DisplayOrder = 2 };
        db.ScoreBands.AddRange(bandA, bandB);
        await db.SaveChangesAsync();

        var template = new PathTemplate { CertId = cert.CertId, FromBandCode = "A2", ToBandCode = "B2", Name = "A2 to B2" };
        db.PathTemplates.Add(template);
        await db.SaveChangesAsync();

        var module = new PathModule { TemplateId = template.TemplateId, SkillId = skill.SkillId, CertId = cert.CertId, DisplayOrder = 1, Title = "Grammar" };
        db.PathModules.Add(module);
        await db.SaveChangesAsync();

        var item1 = new PathModuleItem { ModuleId = module.ModuleId, CertId = cert.CertId, TemplateId = template.TemplateId, ItemType = "practice_section", RefSectionId = section.SectionId, DisplayOrder = 1 };
        var item2 = new PathModuleItem { ModuleId = module.ModuleId, CertId = cert.CertId, TemplateId = template.TemplateId, ItemType = "practice_section", RefSectionId = section.SectionId, DisplayOrder = 2 };
        db.PathModuleItems.AddRange(item1, item2);
        await db.SaveChangesAsync();

        var path = new LearnerPath { UserId = userId, CertId = cert.CertId, TemplateId = template.TemplateId, CurrentBandCode = "A2", Status = "active", StartedAt = DateTimeOffset.UtcNow };
        db.LearnerPaths.Add(path);
        await db.SaveChangesAsync();

        var step1 = new LearnerPathStep { LearnerPathId = path.LearnerPathId, PathModuleItemId = item1.ItemId, TemplateId = template.TemplateId, Status = "available" };
        var step2 = new LearnerPathStep { LearnerPathId = path.LearnerPathId, PathModuleItemId = item2.ItemId, TemplateId = template.TemplateId, Status = "locked" };
        db.LearnerPathSteps.AddRange(step1, step2);
        await db.SaveChangesAsync();

        var user = await db.Users.FindAsync(userId);
        user!.ActiveCertId = cert.CertId;
        await db.SaveChangesAsync();

        return (cert.CertId, template.TemplateId, path.LearnerPathId, step1.StepId, step2.StepId);
    }

    [Fact]
    public async Task GetOrCreatePathInsightAsync_CreatesPendingInsight_WithCorrectScopeAndFingerprint()
    {
        var certCode = Unique("PI");
        var userId = await SeedLearnerAsync();
        var (certId, templateId, pathId, step1Id, step2Id) = await SeedLearningPathFixtureAsync(userId, certCode);
        var (service, _) = CreateService();

        var insight = await service.GetOrCreatePathInsightAsync(userId);

        Assert.Equal(userId, insight.UserId);
        Assert.Equal(certId, insight.CertId);
        Assert.Equal(AiInsightScope.Path, insight.Scope);
        Assert.Equal(pathId, insight.LearnerPathId);
        Assert.Null(insight.PracticeSessionId);
        Assert.Null(insight.AttemptId);
        Assert.Equal(AiInsightStatus.Pending, insight.Status);
        Assert.NotNull(insight.SourceFingerprint);
        Assert.Equal(64, insight.SourceFingerprint.Length);
    }

    [Fact]
    public async Task GetOrCreatePathInsightAsync_ReusesInsight_WhenStepsUnchanged()
    {
        var certCode = Unique("PR");
        var userId = await SeedLearnerAsync();
        var (certId, templateId, pathId, step1Id, step2Id) = await SeedLearningPathFixtureAsync(userId, certCode);
        var (service, _) = CreateService();

        var initial = await service.GetOrCreatePathInsightAsync(userId);
        await service.ProcessPendingInsightsAsync();

        var reloaded = await service.GetOrCreatePathInsightAsync(userId);

        Assert.Equal(initial.InsightId, reloaded.InsightId);
        Assert.Equal(initial.SourceFingerprint, reloaded.SourceFingerprint);
    }

    [Fact]
    public async Task GetOrCreatePathInsightAsync_Regenerates_WhenStepCompleted()
    {
        var certCode = Unique("PG");
        var userId = await SeedLearnerAsync();
        var (certId, templateId, pathId, step1Id, step2Id) = await SeedLearningPathFixtureAsync(userId, certCode);
        var (service, _) = CreateService();

        var initial = await service.GetOrCreatePathInsightAsync(userId);
        await service.ProcessPendingInsightsAsync();

        // Complete step 1
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var step = await db.LearnerPathSteps.FindAsync(step1Id);
            step!.Status = "done";
            step.CompletedAt = DateTimeOffset.UtcNow;
            step.Score = 85m;
            await db.SaveChangesAsync();
        }

        // Call again -> fingerprint must change because completed step count changed
        var updated = await service.GetOrCreatePathInsightAsync(userId);

        Assert.NotEqual(initial.InsightId, updated.InsightId);
        Assert.NotEqual(initial.SourceFingerprint, updated.SourceFingerprint);
        Assert.Equal(AiInsightStatus.Pending, updated.Status);
    }

    [Fact]
    public async Task ProcessPendingInsightsAsync_GeneratesPathInsightNarrativeAndRecommendations()
    {
        var certCode = Unique("PN");
        var userId = await SeedLearnerAsync();
        var (certId, templateId, pathId, step1Id, step2Id) = await SeedLearningPathFixtureAsync(userId, certCode);
        var (service, _) = CreateService();

        await service.GetOrCreatePathInsightAsync(userId);

        int processed = await service.ProcessPendingInsightsAsync();
        Assert.True(processed >= 1);

        var doneInsight = await service.GetOrCreatePathInsightAsync(userId);
        Assert.Equal(AiInsightStatus.Done, doneInsight.Status);
        Assert.NotNull(doneInsight.Narrative);
        Assert.NotEmpty(doneInsight.Narrative);
        Assert.NotNull(doneInsight.Trend);
        Assert.NotEmpty(doneInsight.Recommendations);
    }

    [Fact]
    public async Task RetryPathInsightAsync_ResetsFailedInsightToPending()
    {
        var certCode = Unique("PFAIL");
        var userId = await SeedLearnerAsync();
        var (certId, templateId, pathId, step1Id, step2Id) = await SeedLearningPathFixtureAsync(userId, certCode);

        var generator = new FakeAiInsightGenerator { ShouldSimulateFailure = true };
        var (service, _) = CreateService(generator);

        await service.GetOrCreatePathInsightAsync(userId);
        await service.ProcessPendingInsightsAsync();

        var failedInsight = await service.GetOrCreatePathInsightAsync(userId);
        Assert.Equal(AiInsightStatus.Failed, failedInsight.Status);

        // Turn off simulated failure and retry
        generator.ShouldSimulateFailure = false;
        await service.RetryPathInsightAsync(userId);

        var retriedInsight = await service.GetOrCreatePathInsightAsync(userId);
        Assert.Equal(AiInsightStatus.Pending, retriedInsight.Status);

        await service.ProcessPendingInsightsAsync();
        var doneInsight = await service.GetOrCreatePathInsightAsync(userId);
        Assert.Equal(AiInsightStatus.Done, doneInsight.Status);
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
    [Fact]
    public async Task ExtractErrorFingerprints_PrioritizesFlaggedErrors_AndLowestCategoryAccuracy()
    {
        var certCode = Unique("INSERR");
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateService();

        var q1 = new Question { QuestionId = 1, Stem = "Question 1 stem", CategoryId = 10, Explanation = "Exp 1" };
        var q2 = new Question { QuestionId = 2, Stem = "Question 2 stem", CategoryId = 20, Explanation = "Exp 2" };

        var opt1 = new QuestionOption { OptionId = 101, QuestionId = 1, Text = "Opt 1 Wrong", IsCorrect = false };
        var opt1Correct = new QuestionOption { OptionId = 102, QuestionId = 1, Text = "Opt 1 Correct", IsCorrect = true };
        var opt2 = new QuestionOption { OptionId = 201, QuestionId = 2, Text = "Opt 2 Wrong", IsCorrect = false };
        var opt2Correct = new QuestionOption { OptionId = 202, QuestionId = 2, Text = "Opt 2 Correct", IsCorrect = true };

        var answers = new List<AttemptAnswer>
        {
            new AttemptAnswer { QuestionId = 1, SelectedOptionId = 101, IsCorrect = false, IsFlagged = true, Question = q1 },
            new AttemptAnswer { QuestionId = 2, SelectedOptionId = 201, IsCorrect = false, IsFlagged = false, Question = q2 }
        };

        var allOptions = new List<QuestionOption> { opt1, opt1Correct, opt2, opt2Correct };
        var catAccuracyRows = new List<vw_UserCategoryAccuracy>
        {
            new vw_UserCategoryAccuracy { UserId = userId, CategoryId = 20, Answered = 10, Correct = 2 },
            new vw_UserCategoryAccuracy { UserId = userId, CategoryId = 10, Answered = 10, Correct = 8 }
        };

        var fingerprints = service.ExtractErrorFingerprints(answers, allOptions, catAccuracyRows);
        Assert.Equal(2, fingerprints.Count);
        // Flagged error should be first
        Assert.True(fingerprints[0].WasFlagged);
        Assert.Equal("Question 1 stem", fingerprints[0].Stem);
        Assert.Equal("Opt 1 Wrong", fingerprints[0].ChosenOptionText);
        Assert.Equal("Opt 1 Correct", fingerprints[0].CorrectOptionText);
    }
}