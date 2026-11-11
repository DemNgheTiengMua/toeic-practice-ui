using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #239: the productive practice seam. Four things are asserted here rather than on a page, because
/// each is a fact the server owns:
/// <list type="number">
/// <item>The word count the composer shows is the one the server stores — <em>one</em> rule, so a
/// double space, a hyphen and a newline cannot mean two different numbers on two sides.</item>
/// <item>The recording window is the task's own declared one, and a client that ignores it is refused
/// on the server. A window enforced only by JavaScript is a window a modified client removes.</item>
/// <item>A failed grading produces no score and does not touch the ledger — and the retry does not
/// attempt a charge at all. The ledger is counted, not inspected: #217 recorded that "no charge" could
/// not be proved by inserting one, because the schema refuses a second <c>ai_grading</c> row on an
/// attempt. Counting the calls proves the code path, which is the part this ticket changes.</item>
/// <item>A result from one graded task says so; a result from two does not.</item>
/// </list>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductivePracticeTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    // ------------------------------------------------------------------ 1. the word count

    /// <summary>
    /// <b>The input that would pass a wrong rule:</b> <c>"one  two"</c> (two spaces), counted by a
    /// regex that splits on whitespace runs, agrees; <c>"one\u00A0two"</c> (one no-break space) does
    /// not — <c>\s</c> splits it, the server's four separators do not, so the page would say "2 words"
    /// above a stored 1. Hyphens and newlines are here for the same reason: a rule that strips
    /// punctuation or splits on <c>\s</c> answers these three strings differently from the server.
    /// </summary>
    [Fact]
    public void The_word_count_is_the_servers_rule_and_not_a_whitespace_split()
    {
        Assert.Equal(2, WordCount.Count("one  two"));
        Assert.Equal(1, WordCount.Count("well-known"));
        Assert.Equal(2, WordCount.Count("first\nsecond"));
        Assert.Equal(0, WordCount.Count("   \r\n\t "));
        Assert.Equal(0, WordCount.Count(null));

        // The trap: \s and \p{Zs} both treat a no-break space as a separator. The server's rule does
        // not, and a no-break space is exactly what a learner pastes from a word processor.
        Assert.Equal(1, WordCount.Count("one\u00A0two"));
    }

    // ------------------------------------------------------------------ 2. the recording window

    /// <summary>
    /// <b>The input that would pass a client-only guard:</b> a request carrying a 45-second recording
    /// for a task that declares 30 seconds. The browser stops the recorder at 30, so every test that
    /// drives the page passes; a client that ignores the countdown uploads 45 and, before this guard,
    /// the server stored it. The declared window is the row's <c>ResponseSeconds</c>.
    /// </summary>
    [Fact]
    public async Task A_recording_longer_than_the_task_declares_is_refused_by_the_server()
    {
        var certCode = Unique("SPW1");
        var (_, examId, q1Id, _) = await SeedSpeakingExamAsync(certCode, prepSeconds: 45, responseSeconds: 30);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _, _, _) = CreateSpeakingServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        using var tooLong = AudioStream();
        var ex = await Assert.ThrowsAsync<SpeakingPracticeException>(() =>
            service.SaveAudioAsync(userId, attempt.AttemptId, q1Id, tooLong, "audio/webm", "q1.webm", 45_000));
        Assert.Equal(SpeakingPracticeException.AudioTooLong, ex.Code);

        // Exactly the window is inside it: the boundary is inclusive, so a recorder that stops on the
        // second still saves its take.
        using var atTheLimit = AudioStream();
        var saved = await service.SaveAudioAsync(userId, attempt.AttemptId, q1Id, atTheLimit, "audio/webm", "q1.webm", 30_000);
        Assert.Equal(q1Id, saved.QuestionId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var fr = await db.FreeResponses.SingleAsync(r => r.AttemptId == attempt.AttemptId && r.QuestionId == q1Id);
        Assert.Equal(30_000, fr.DurationMs);
    }

    // ------------------------------------------------------------------ 3. a failed grading

    /// <summary>
    /// <b>The input that would pass a weaker test:</b> asserting only that the attempt is not graded.
    /// A failed grading that had already written a score, or that had charged for the retry, passes
    /// that. So: no <c>AiGradingScores</c>, no <c>OverallScaled</c>, no <c>BandCode</c>, the reason
    /// carried on the <c>AiGrading</c> row, the response settled, and the ledger <em>called zero times
    /// by the retry</em> — counted, because the schema's unique index on
    /// <c>(AttemptId, Reason)</c> makes a second charge unrepresentable and therefore unobservable.
    /// </summary>
    [Fact]
    public async Task A_failed_grading_scores_nothing_and_a_retry_never_attempts_a_charge()
    {
        var certCode = Unique("WRF1");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);

        var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = new CountingLedger(db);
        var service = new WritingPracticeService(db, new LearnerService(db), ledger);
        var processor = new AiGradingProcessor(DbContextFactory.Create(database.ConnectionString), new FakeAiGrader());

        var attempt = await service.StartAttemptAsync(userId, examId);
        // Both late answers carry the fail marker, so the whole attempt's grading fails and there is no
        // "one task succeeded" noise in the middle of what this test is about.
        await service.SaveDraftAsync(userId, attempt.AttemptId, q1Id, "Here is text with [simulate_fail] in it.");
        await service.SaveDraftAsync(userId, attempt.AttemptId, q2Id, "Here is more text with [simulate_fail] too.");

        var chargesAfterSubmit = 0;
        await service.SubmitAttemptAsync(userId, attempt.AttemptId);

        await using (var afterSubmit = DbContextFactory.Create(database.ConnectionString))
        {
            chargesAfterSubmit = await afterSubmit.CreditTransactions.CountAsync(t => t.AttemptId == attempt.AttemptId);
            Assert.Equal(1, chargesAfterSubmit);
        }

        var chargeCallsBeforeRetry = ledger.AiGradingCharges;

        await processor.ProcessPendingGradingsAsync();

        await using (var db2 = DbContextFactory.Create(database.ConnectionString))
        {
            var grading = await db2.AiGradings.SingleAsync(g =>
                g.FreeResponse.AttemptId == attempt.AttemptId && g.FreeResponse.QuestionId == q1Id);
            Assert.Equal("failed", grading.Status);
            Assert.NotNull(grading.FailureReason);
            Assert.Null(grading.OverallScaled);
            Assert.Null(grading.BandCode);
            Assert.Equal(0, await db2.AiGradingScores.CountAsync(s => s.GradingId == grading.GradingId));

            // The response is settled (it has no `failed` state), so the attempt is allowed to finish —
            // the distinction that matters is carried on the AiGrading row.
            var response = await db2.FreeResponses.SingleAsync(r => r.FreeResponseId == grading.FreeResponseId);
            Assert.Equal("graded", response.Status);

            var settled = await db2.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
            Assert.NotEqual("graded", settled.Status);
            Assert.Null(settled.TotalScore);
        }

        // The retry is reachable from the failure state and does not attempt a charge.
        var pending = await service.GetGradingPendingAsync(userId, attempt.AttemptId);
        Assert.True(pending.CanRetry, "the failure state must offer a retry");
        Assert.Equal(2, pending.FailedResponses);

        await service.RetryGradingAsync(userId, attempt.AttemptId);

        Assert.Equal(chargeCallsBeforeRetry, ledger.AiGradingCharges);

        await using var db3 = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(chargesAfterSubmit, await db3.CreditTransactions.CountAsync(t => t.AttemptId == attempt.AttemptId));
        var balance = await db3.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(1, balance);   // 2 granted - 1 charged once, and the retry moved nothing
    }

    // ------------------------------------------------------------------ 4. one task is not a level

    /// <summary>
    /// <b>The input that would pass a "the word estimate appears" assertion:</b> a two-task attempt,
    /// which is labelled the same way and therefore claims nothing false-looking. The label is a fact
    /// about how many tasks were graded, so both directions are asserted here: one task IS labelled,
    /// two tasks are NOT.
    /// </summary>
    [Fact]
    public async Task A_result_from_one_graded_task_is_labelled_and_a_two_task_result_is_not()
    {
        var certCode = Unique("WRF2");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 4);
        var (service, _, processor, _) = CreateWritingServices();

        var attempt = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, attempt.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SubmitAttemptAsync(userId, attempt.AttemptId);
        await processor.ProcessPendingGradingsAsync();

        var oneTask = await service.GetAttemptReviewAsync(userId, attempt.AttemptId);
        Assert.Equal(1, oneTask.GradedQuestions);
        Assert.True(oneTask.IsEstimatedFromOneTask, "a single graded task must be labelled as such");

        var second = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, second.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SaveDraftAsync(userId, second.AttemptId, q2Id, "In my opinion, working from home provides greater flexibility and boosts personal productivity significantly.");
        await service.SubmitAttemptAsync(userId, second.AttemptId);
        await processor.ProcessPendingGradingsAsync();

        var twoTasks = await service.GetAttemptReviewAsync(userId, second.AttemptId);
        Assert.Equal(2, twoTasks.GradedQuestions);
        Assert.False(twoTasks.IsEstimatedFromOneTask, "two graded tasks are not one task's estimate");
    }

    // ------------------------------------------------------------------ 5. prior submissions

    /// <summary>
    /// <b>The input that would pass a "history is rendered" check:</b> the one latest graded attempt,
    /// which is all the door showed before this ticket. Both attempts the learner has made on the exam
    /// are listed, newest first, and each carries the route that re-opens it — a graded one to its
    /// feedback, an unfinished one back into the sitting.
    /// </summary>
    [Fact]
    public async Task Every_prior_attempt_on_an_exam_is_listed_and_carries_its_route()
    {
        var certCode = Unique("WRF3");
        var (_, examId, q1Id, _) = await SeedWritingExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 4);
        var (service, _, processor, _) = CreateWritingServices();

        var first = await service.StartAttemptAsync(userId, examId);
        await service.SaveDraftAsync(userId, first.AttemptId, q1Id, "A woman is reading a book near the window.");
        await service.SubmitAttemptAsync(userId, first.AttemptId);
        await processor.ProcessPendingGradingsAsync();

        var second = await service.StartAttemptAsync(userId, examId);

        var instructions = await service.GetInstructionsAsync(userId, examId);
        Assert.Equal(2, instructions.Attempts.Count);

        // Newest first.
        Assert.Equal(second.AttemptId, instructions.Attempts[0].AttemptId);
        Assert.Equal(first.AttemptId, instructions.Attempts[1].AttemptId);

        var graded = Assert.Single(instructions.Attempts, a => a.AttemptId == first.AttemptId);
        Assert.True(graded.CanReview);
        Assert.Equal("/writing/review/" + first.AttemptId, graded.ReopenPath("/writing"));

        var open = Assert.Single(instructions.Attempts, a => a.AttemptId == second.AttemptId);
        Assert.False(open.CanReview);
        Assert.Equal("/writing/take/" + second.AttemptId, open.ReopenPath("/writing"));
    }

    // ------------------------------------------------------------------ fixtures

    private static MemoryStream AudioStream() =>
        new(System.Text.Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....data....take"));

    private (WritingPracticeService service, LearnerService learners, AiGradingProcessor processor, FakeAiGrader grader) CreateWritingServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var grader = new FakeAiGrader();
        return (new WritingPracticeService(db, new LearnerService(db)), new LearnerService(db),
            new AiGradingProcessor(db, grader), grader);
    }

    private (SpeakingPracticeService service, LocalFileStore store, AiGradingProcessor processor, FakeAiGrader grader) CreateSpeakingServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var store = new LocalFileStore(Path.Combine(Path.GetTempPath(), "ee_239_" + Guid.NewGuid().ToString("N")));
        var grader = new FakeAiGrader();
        return (new SpeakingPracticeService(db, new LearnerService(db), store), store,
            new AiGradingProcessor(db, grader), grader);
    }

    private async Task<int> SeedLearnerAsync(string certCode, int startingCredits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
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
    /// A writing practice paper as two legacy tasks.
    /// <para>
    /// <b>#269: NOT migrated to the pool, and the reason is production code rather than this
    /// fixture.</b> The ids this fixture returns are the addresses the tests pass to
    /// <c>WritingPracticeService</c>, and the service reads <c>fr.Question</c> on four paths that a pool
    /// answer leaves null:
    /// <list type="bullet">
    /// <item><c>SaveDraftAsync</c> resolves the position through <c>LoadPaperPositionAsync</c>, which
    /// works, but <c>GetGradingPendingAsync</c> dereferences <c>fr.Question.DisplayOrder</c> and
    /// <c>fr.QuestionId!.Value</c> — the probe throws "Nullable object must have a value".</item>
    /// <item><c>GetAttemptReviewAsync</c> orders by <c>fr.Question.DisplayOrder</c> and reads
    /// <c>fr.Question.Stem</c>/<c>.Section.Name</c>, so it throws before the review exists.</item>
    /// <item><c>AiGradingProcessor</c> reads <c>grading.FreeResponse.Question.SectionId</c> and
    /// <c>.Section.Modality</c>; on a pool answer both are null and every grading fails with a
    /// NullReferenceException, which then keeps the attempt <c>submitted</c> rather than graded.</item>
    /// <item><c>SpeakingPracticeService.SaveAudioAsync</c> looks the task up in <c>db.Questions</c>
    /// directly, so a pool speaking take renders but cannot be saved ("Speaking question N not found
    /// in this exam").</item>
    /// </list>
    /// Migrating the fixture before those four readers move would turn four green tests red for a
    /// reason this ticket does not own. The fixture stays legacy until they do; #269 deletes it with
    /// the tables.
    /// </para>
    /// </summary>
    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedWritingExamAsync(string certCode)
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

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sW, 'GRAM', N'Grammar', N'Band 5: accurate simple sentences. Band 3: frequent errors.', 5.0, 1.0, 1),
              (@cert, @sW, 'VOCAB', N'Vocabulary', N'Band 5: adequate range. Band 3: limited range.', 5.0, 1.0, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, MinWords, MaxWords, RequiredKeywords, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sW, N'Write a sentence describing the picture.', 'A2', 'free_response', 5, 25, N'woman, window', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec2, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sW, N'Write an essay giving your opinion on remote work.', 'B2', 'free_response', 30, 300, 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();

        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    /// <summary>
    /// A speaking practice paper as two legacy tasks. See <see cref="SeedWritingExamAsync"/> for why
    /// #269 leaves this fixture legacy: <c>SpeakingPracticeService.SaveAudioAsync</c> resolves the
    /// recording window from <c>db.Questions</c>, so a pool speaking take cannot be saved at all.
    /// </summary>
    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedSpeakingExamAsync(
        string certCode, int prepSeconds, int responseSeconds)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'SPEA', N'Speaking', 'speaking', 1, 20);
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'SPEA');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Read aloud', 1, 1),
              (@sS, @cert, 'speaking', 'S2', N'Describe picture', 1, 2);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S1');
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S2');

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sS, 'PRON', N'Pronunciation', 3.0, 1.0, 1);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Speaking Practice {certCode}', 'published', 20, 'speaking_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sS, N'Read the announcement aloud.', 'A2', 'free_response', {prepSeconds}, {responseSeconds}, 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec2, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, PrepSeconds, ResponseSeconds, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sS, N'Describe what you see.', 'B1', 'free_response', {prepSeconds}, {responseSeconds}, 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();

        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    /// <summary>
    /// The real ledger over the SAME context the service uses, so the charge joins the service's open
    /// transaction, plus a count of the calls that would move credits. A retry that calls
    /// <c>ChargeAiGradingAsync</c> is idempotent — <c>UX_CreditTxn_Attempt_Reason</c> refuses a second
    /// row and the method returns the first receipt — so the ledger's own state cannot tell the
    /// difference. The call count can, and that is what this ticket changes.
    /// </summary>
    private sealed class CountingLedger(CompanionDbContext db) : ICreditLedger
    {
        private readonly CreditLedger _inner = new(db);

        public int AiGradingCharges { get; private set; }

        public Task<int> GetBalanceAsync(int userId, CancellationToken cancellationToken = default) =>
            _inner.GetBalanceAsync(userId, cancellationToken);

        public Task<CreditReceipt> ChargeAiGradingAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default)
        {
            AiGradingCharges++;
            return _inner.ChargeAiGradingAsync(userId, attemptId, amount, cancellationToken);
        }

        public Task<CreditReceipt> ChargeMockExamAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Writing practice never charges for a mock sitting.");

        public Task<CreditReceipt> ChargeMentorBookingAsync(int userId, int bookingId, int priceCredits, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditReceipt> RefundBookingAsync(int userId, int bookingId, int amount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditReceipt> GrantPurchaseAsync(int userId, int orderId, int amount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditReceipt> AdjustCreditsAsync(int targetUserId, int? adminUserId, int delta, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CreditLedgerEntryDto>> GetLedgerHistoryAsync(int userId, CancellationToken cancellationToken = default) =>
            _inner.GetLedgerHistoryAsync(userId, cancellationToken);
    }
}
