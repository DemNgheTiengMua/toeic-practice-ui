using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
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

    /// <summary>
    /// Two productive tasks on one writing-practice paper.
    /// <para>
    /// #272: the tasks are pool items whose versions carry the word window, and the ids the tests address
    /// are their <c>ExamItemId</c>s. The W1 section override and the per-task word limits are unchanged -
    /// only the content model moved.
    /// </para>
    /// </summary>
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
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sW, 'GRAM', N'Grammar', N'Band 5: accurate simple sentences. Band 3: frequent errors obscure meaning.', 5.0, 1.0, 1),
              (@cert, @sW, 'VOCAB', N'Vocabulary', N'Band 5: adequate range. Band 3: limited range, repetition.', 5.0, 1.0, 2);
            DECLARE @crit1 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'GRAM');
            DECLARE @crit2 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'VOCAB');

            -- Section override for W1: MaxScore 3.0
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore) VALUES
              (@sec1, @crit1, @sW, 3.0),
              (@sec1, @crit2, @sW, 3.0);

            -- Exam
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section1 = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "W1");
        var section2 = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "W2");

        var q1 = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section1.SectionId, "writing",
            "Write a sentence describing the picture.", displayOrder: 1,
            minWords: 5, maxWords: 25, level: "A2");
        var q2 = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section2.SectionId, "writing",
            "Write an essay giving your opinion on remote work.", displayOrder: 1,
            minWords: 30, maxWords: 300, level: "B2");

        return (cert.CertId, exam.ExamId, q1.ExamItemId, q2.ExamItemId);
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
    public async Task FreeDiagnosticWriting_Can_Start_And_Submit_Without_Credits_Or_LedgerEntries()
    {
        var certCode = Unique("WRFD");
        var (_, examId, q1Id, _) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await database.SqlAsync($"""
            UPDATE Exams
            SET SourceName = N'{WritingPracticeService.FreeDiagnosticSourceName}'
            WHERE ExamId = {examId};
            """);
        var (service, _, _, _) = CreateServices();

        var instructions = await service.GetInstructionsAsync(userId, examId);
        Assert.Equal(0, instructions.CreditCost);
        Assert.Equal(0, instructions.CreditBalance);

        var attempt = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, attempt.AttemptId, q1Id, "A clear diagnostic writing response.");
        await service.SubmitAttemptAsync(userId, attempt.AttemptId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var transactions = await db.CreditTransactions.Where(t => t.UserId == userId).ToListAsync();
        Assert.Empty(transactions);
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
        var draft = await db.FreeResponses.SingleAsync(fr => fr.AttemptId == info.AttemptId && fr.ExamItemId == q1Id);
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

    /// <summary>
    /// #269: the grading-status reader for a paper authored on the pool.
    /// <para>
    /// Every writing/speaking screen that reports "graded / pending / failed" read
    /// <c>fr.QuestionId!.Value</c> and <c>fr.Question.DisplayOrder</c>. A pool response has neither, so
    /// the page threw "Nullable object must have a value" - after the grading had already succeeded and
    /// the credit had already been taken. Found by driving the real browser flow.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetGradingPendingAsync_reports_a_pool_sitting_with_no_question_rows()
    {
        var certCode = Unique("WRPool");
        int userId = 0;

        int attemptId;
        int examId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            // The learner is seeded AFTER the certificate, so its ActiveCertId points at this one - a
            // writing attempt is started for the learner's ACTIVE certificate and nothing else.
            userId = await SeedLearnerAsync(certCode, startingCredits: 2);

            var skill = new Skill
            {
                CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing",
                DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                CertId = cert.CertId, SkillId = skill.SkillId, Modality = "writing",
                Code = "W1", Name = "Sentence from picture", DisplayOrder = 1,
                ExpectedQuestionCount = 1, LastWrittenBy = userId,
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            db.GradingCriteria.Add(new GradingCriterion
            {
                SkillId = skill.SkillId, CertId = cert.CertId, Code = "W_TASK", Name = "Task achievement",
                MaxScore = 3m, Weight = 1m, DisplayOrder = 1,
                RubricDescription = "How fully the task is achieved.",
            });
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId, Name = $"Writing {certCode}", Status = "published",
                DurationMinutes = 60, Purpose = "writing_practice", QuestionCount = 1,
                LastWrittenBy = userId,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            examId = exam.ExamId;

            var position = await PoolFixture.AddProductiveAsync(
                db, exam.ExamId, cert.CertId, section.SectionId, "writing",
                "Write a sentence about the picture.", displayOrder: 1, minWords: 5, maxWords: 40);

            var learners = new LearnerService(db);
            var writing = new WritingPracticeService(db, learners);
            var info = await writing.StartAttemptAsync(userId, exam.ExamId);
            attemptId = info.AttemptId;
            await writing.SaveDraftAsync(userId, attemptId, position.ExamItemId,
                "The team is discussing the new proposal in the meeting room.");
            await writing.SubmitAttemptAsync(userId, attemptId);

            // #269: the legacy Questions table is gone, so "no legacy question was seeded" is a schema
            // fact rather than an assertion.
        }

        var (service, _, gradingProcessor, _) = CreateServices();
        await gradingProcessor.ProcessPendingGradingsAsync();

        var pending = await service.GetGradingPendingAsync(userId, attemptId);

        // The address reported is the position's, and the counts describe real work.
        Assert.Equal(1, pending.TotalResponses);
        Assert.Equal(1, pending.GradedResponses);
        Assert.Equal(0, pending.FailedResponses);
    }

    /// <summary>
    /// #269: a writing route must refuse a speaking paper.
    /// <para>
    /// Writing and speaking tasks both render as <c>free_response</c>, so every filter that asked only
    /// for that format accepted either modality. Measured against the running API: GET
    /// <c>/api/learner/writing/attempts/{speakingAttemptId}</c> answered 200 with the speaking exam,
    /// its three speaking tasks and their prompts - a learner's writing screen listing read-aloud and
    /// picture-description tasks, and a writing submission writing speak rows.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetAttemptTake_refuses_a_speaking_paper_through_the_writing_route()
    {
        var certCode = Unique("WRMod");
        int speakingAttemptId;
        int userId = 0;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            // Seeded AFTER the certificate: a practice attempt starts for the learner's ACTIVE
            // certificate, and seeding first leaves ActiveCertId null - "exam not found".
            userId = await SeedLearnerAsync(certCode, startingCredits: 3);

            var skill = new Skill
            {
                CertId = cert.CertId, Code = "SPEA", Name = "Speaking", Modality = "speaking",
                DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                CertId = cert.CertId, SkillId = skill.SkillId, Modality = "speaking",
                Code = "S1", Name = "Read a text aloud", DisplayOrder = 1,
                ExpectedQuestionCount = 1, LastWrittenBy = userId,
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId, Name = $"Speaking {certCode}", Status = "published",
                DurationMinutes = 20, Purpose = "speaking_practice", QuestionCount = 1,
                LastWrittenBy = userId,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            await PoolFixture.AddProductiveAsync(
                db, exam.ExamId, cert.CertId, section.SectionId, "speaking",
                "Read the announcement aloud.", displayOrder: 1, prepSeconds: 10, responseSeconds: 45);

            var speakingTemp = Path.Combine(Path.GetTempPath(), "ee-speak-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(speakingTemp);
            var speaking = new SpeakingPracticeService(db, new LearnerService(db), new LocalFileStore(speakingTemp));
            speakingAttemptId = (await speaking.StartAttemptAsync(userId, exam.ExamId)).AttemptId;
        }

        var (writing, _, _, _) = CreateServices();

        // The writing take route must not answer for a speaking paper.
        var ex = await Assert.ThrowsAsync<WritingPracticeException>(() =>
            writing.GetAttemptTakeAsync(userId, speakingAttemptId, questionId: null));
        Assert.Equal(WritingPracticeException.QuestionNotFound, ex.Code);
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
        var failedGrading = await db.AiGradings.Include(g => g.FreeResponse).SingleAsync(g => g.FreeResponse.AttemptId == info.AttemptId && g.FreeResponse.ExamItemId == q1Id);
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
