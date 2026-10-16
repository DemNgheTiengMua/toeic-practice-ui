using System;
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
public sealed class WritingPracticeServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (WritingPracticeService service, LearnerService learners, AiGradingProcessor gradingProcessor, FakeAiGrader grader) CreateServices(IAiGrader? customGrader = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var grader = new FakeAiGrader();
        var gradingProcessor = new AiGradingProcessor(db, customGrader ?? grader);
        var service = new WritingPracticeService(db, learners);
        return (service, learners, gradingProcessor, grader);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null, int startingCredits = 0)
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
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Test setup credits');
                """);
        }

        return userId;
    }

    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedWritingExamFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'WRIT', N'Writing', 'writing', 1, 60);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sW, @cert, 'writing', 'W1', N'Sentence from picture', 1, 1),
              (@sW, @cert, 'writing', 'W2', N'Essay', 1, 2);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W1');
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W2');

            -- Criteria
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sW, 'GRAM', N'Grammar', 5.0, 1.0, 1),
              (@cert, @sW, 'VOCAB', N'Vocabulary', 5.0, 1.0, 2);
            DECLARE @crit1 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'GRAM');
            DECLARE @crit2 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'VOCAB');

            -- Section override for W1: MaxScore 3.0
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore) VALUES
              (@sec1, @crit1, @sW, 3.0),
              (@sec1, @crit2, @sW, 3.0);

            -- Exam
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Question 1: Sentence from picture
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sW, N'Write a sentence describing the picture.', 1, 'free_response', 5, 25, 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            -- Question 2: Essay
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec2, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sW, N'Write an essay giving your opinion on remote work.', 3, 'free_response', 30, 300, 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();

        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    [Fact]
    public async Task StartAttempt_when_insufficient_credits_throws_before_charging()
    {
        var certCode = Unique("WR01");
        var (_, examId, _, _) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 0);
        var (service, _, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<WritingPracticeException>(() => service.StartAttemptAsync(userId, examId));
        Assert.Equal(WritingPracticeException.InsufficientCredits, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attemptsCount = await db.ExamAttempts.CountAsync(a => a.UserId == userId);
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task StartAttempt_creates_in_progress_attempt_and_drafts_without_charging_credits()
    {
        var certCode = Unique("WR02");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 5);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        Assert.True(info.AttemptId > 0);
        Assert.Equal(AttemptStatus.InProgress, info.Status);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // Balance remains 5: drafts are not charged
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(5, balance);

        // Pre-populated drafts exist with SubmittedAt == NULL
        var drafts = await db.FreeResponses.Where(fr => fr.AttemptId == info.AttemptId).ToListAsync();
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d =>
        {
            Assert.Equal("draft", d.Status);
            Assert.Null(d.SubmittedAt);
        });
    }

    [Fact]
    public async Task SaveDraft_updates_text_and_records_DraftSavedAt_keeping_SubmittedAt_null()
    {
        var certCode = Unique("WR03");
        var (_, examId, q1Id, _) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        var saveResult = await service.SaveDraftAsync(userId, info.AttemptId, q1Id, "A woman is reading a book near the window.");

        Assert.Equal(q1Id, saveResult.QuestionId);
        Assert.True(saveResult.WordCount > 0);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var draft = await db.FreeResponses.SingleAsync(fr => fr.AttemptId == info.AttemptId && fr.QuestionId == q1Id);
        Assert.Equal("A woman is reading a book near the window.", draft.ResponseText);
        Assert.Equal("draft", draft.Status);
        Assert.Null(draft.SubmittedAt);
        Assert.NotNull(draft.DraftSavedAt);
    }

    [Fact]
    public async Task SubmitAttempt_transitions_drafts_charges_one_credit_and_queues_ai_grading()
    {
        var certCode = Unique("WR04");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, info.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SaveDraftAsync(userId, info.AttemptId, q2Id, "In my opinion, working from home provides greater flexibility and boosts personal productivity significantly.");

        var submitResult = await service.SubmitAttemptAsync(userId, info.AttemptId);
        Assert.Equal(AttemptStatus.Submitted, submitResult.Status);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // Balance decreased by exactly 1
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(2, balance);

        // Exactly one ai_grading credit txn
        var txns = await db.CreditTransactions.Where(t => t.AttemptId == info.AttemptId).ToListAsync();
        Assert.Single(txns);
        Assert.Equal("ai_grading", txns[0].Reason);
        Assert.Equal(-1, txns[0].Delta);

        // Drafts transitioned to pending_ai with SubmittedAt != null
        var responses = await db.FreeResponses.Where(r => r.AttemptId == info.AttemptId).ToListAsync();
        Assert.All(responses, r =>
        {
            Assert.Equal("pending_ai", r.Status);
            Assert.NotNull(r.SubmittedAt);
        });

        // Exactly one AiGradings row per response
        var gradings = await db.AiGradings.Where(g => responses.Select(r => r.FreeResponseId).Contains(g.FreeResponseId)).ToListAsync();
        Assert.Equal(2, gradings.Count);
        Assert.All(gradings, g => Assert.Equal("pending", g.Status));
    }

    [Fact]
    public async Task ProcessPendingGradings_grades_responses_sets_scores_and_marks_attempt_graded_without_band_or_total_score()
    {
        var certCode = Unique("WR05");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _, gradingProcessor, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, info.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SaveDraftAsync(userId, info.AttemptId, q2Id, "In my opinion, working from home provides greater flexibility and boosts personal productivity significantly.");
        await service.SubmitAttemptAsync(userId, info.AttemptId);

        var processed = await gradingProcessor.ProcessPendingGradingsAsync();
        Assert.True(processed >= 2);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("graded", attempt.Status);

        // Invariant: TotalScore and BandCode MUST remain NULL for writing_practice!
        Assert.Null(attempt.TotalScore);
        Assert.Null(attempt.BandCode);

        // All free responses marked 'graded' via trigger
        var responses = await db.FreeResponses.Where(r => r.AttemptId == info.AttemptId).ToListAsync();
        Assert.All(responses, r => Assert.Equal("graded", r.Status));

        // AiGradingScores created and bounded by MaxScore
        var scores = await db.AiGradingScores.Include(s => s.AiGrading).Where(s => s.AiGrading.FreeResponse.AttemptId == info.AttemptId).ToListAsync();
        Assert.NotEmpty(scores);
        Assert.All(scores, s => Assert.True(s.Score >= 0));
    }

    [Fact]
    public async Task Failed_grading_can_be_retried_reusing_grading_row_without_additional_charges()
    {
        var certCode = Unique("WR06");
        var (_, examId, q1Id, _) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _, gradingProcessor, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        // Include simulate_fail marker to trigger failure in FakeAiGrader
        await service.SaveDraftAsync(userId, info.AttemptId, q1Id, "Here is text with [simulate_fail] to simulate error.");
        await service.SubmitAttemptAsync(userId, info.AttemptId);

        // Process pending -> should fail
        await gradingProcessor.ProcessPendingGradingsAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var failedGrading = await db.AiGradings.Include(g => g.FreeResponse).SingleAsync(g => g.FreeResponse.AttemptId == info.AttemptId && g.FreeResponse.QuestionId == q1Id);
        Assert.Equal("failed", failedGrading.Status);
        Assert.NotNull(failedGrading.FailureReason);
        var initialGradingId = failedGrading.GradingId;
        var initialAttemptCount = failedGrading.AttemptCount;

        // Check pending view reflects failure
        var pendingView = await service.GetGradingPendingAsync(userId, info.AttemptId);
        Assert.True(pendingView.CanRetry);
        Assert.Equal(1, pendingView.FailedResponses);

        // Call Retry
        await service.RetryGradingAsync(userId, info.AttemptId);

        // Verify row was reused and no extra charge occurred
        await using var dbAfterRetry = DbContextFactory.Create(database.ConnectionString);
        var retriedGrading = await dbAfterRetry.AiGradings.SingleAsync(g => g.GradingId == initialGradingId);
        Assert.Equal("pending", retriedGrading.Status);
        Assert.Equal(initialAttemptCount + 1, retriedGrading.AttemptCount);
        Assert.Null(retriedGrading.FailureReason);

        // Verify balance was NOT debited again
        var balance = await dbAfterRetry.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(1, balance); // 2 - 1 (initial submit) = 1, unchanged by retry!
    }

    [Fact]
    public async Task GetAttemptReview_computes_totals_by_code_and_returns_per_criterion_scores()
    {
        var certCode = Unique("WR07");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, gradingProcessor, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, info.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SaveDraftAsync(userId, info.AttemptId, q2Id, "In my opinion, working from home provides greater flexibility and boosts personal productivity significantly.");
        await service.SubmitAttemptAsync(userId, info.AttemptId);
        await gradingProcessor.ProcessPendingGradingsAsync();

        var review = await service.GetAttemptReviewAsync(userId, info.AttemptId);

        Assert.Equal(info.AttemptId, review.AttemptId);
        Assert.Equal(2, review.TotalQuestions);
        Assert.Equal(2, review.AnsweredQuestions);
        Assert.True(review.TotalScore > 0);
        Assert.True(review.MaxScore > 0);
        Assert.Equal(2, review.Questions.Count);

        // Question 1 has SectionCriteria overrides (MaxScore 3.0 each for 2 criteria = 6.0)
        var q1 = review.Questions.Single(q => q.QuestionId == q1Id);
        Assert.Equal(6.0m, q1.MaxScore);
        Assert.Equal(2, q1.Criteria.Count);
        Assert.All(q1.Criteria, c =>
        {
            Assert.True(c.Score <= c.MaxScore);
            Assert.NotNull(c.Comment);
        });

        // Question 2 has general criteria (MaxScore 5.0 each for 2 criteria = 10.0)
        var q2 = review.Questions.Single(q => q.QuestionId == q2Id);
        Assert.Equal(10.0m, q2.MaxScore);
        Assert.Equal(2, q2.Criteria.Count);

        // Overall total score equals sum of question totals
        Assert.Equal(q1.TotalScore + q2.TotalScore, review.TotalScore);
        Assert.Equal(16.0m, review.MaxScore);
    }
}
