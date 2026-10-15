using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SpeakingPracticeServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (SpeakingPracticeService service, LearnerService learners, LocalFileStore fileStore, FakeAiGrader grader) CreateServices(IAiGrader? customGrader = null)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var tempDir = Path.Combine(Path.GetTempPath(), "extra_efficiency_audio_tests_" + Guid.NewGuid().ToString("N"));
        var fileStore = new LocalFileStore(tempDir);
        var grader = new FakeAiGrader();
        var service = new SpeakingPracticeService(db, learners, fileStore, customGrader ?? grader);
        return (service, learners, fileStore, grader);
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

    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedSpeakingExamFixtureAsync(string certCode)
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

            -- Criteria
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sS, 'PRON', N'Pronunciation', 3.0, 1.0, 1),
              (@cert, @sS, 'INT', N'Intonation & Stress', 3.0, 1.0, 2);
            DECLARE @crit1 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'PRON');
            DECLARE @crit2 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'INT');

            -- Exam
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Speaking Practice {certCode}', 'published', 20, 'speaking_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Question 1: Read aloud
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sS, N'Read the following announcement out loud.', 1, 'free_response', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            -- Question 2: Describe picture
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
            VALUES (@exam, @cert, @sec2, 2, '/img/sample-picture.jpg', N'Office setting');
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sS, N'Describe what you see in the picture.', 2, 'free_response', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();

        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    [Fact]
    public void AudioValidation_validates_type_and_size()
    {
        // Empty file rejected
        var (validEmpty, errEmpty) = AudioValidation.Validate(0, "audio/webm", "test.webm");
        Assert.False(validEmpty);
        Assert.Contains("empty", errEmpty, StringComparison.OrdinalIgnoreCase);

        // Size > 10MB rejected
        var (validTooBig, errTooBig) = AudioValidation.Validate(11 * 1024 * 1024, "audio/webm", "test.webm");
        Assert.False(validTooBig);
        Assert.Contains("exceeds limit", errTooBig, StringComparison.OrdinalIgnoreCase);

        // Disallowed extensions rejected
        var (validExe, errExe) = AudioValidation.Validate(500, "application/octet-stream", "malicious.exe");
        Assert.False(validExe);
        Assert.Contains("format", errExe, StringComparison.OrdinalIgnoreCase);

        // Allowed formats accepted
        var (validWebm, _) = AudioValidation.Validate(1024, "audio/webm", "recording.webm");
        Assert.True(validWebm);

        var (validWav, _) = AudioValidation.Validate(2048, "audio/wav", "voice.wav");
        Assert.True(validWav);

        var (validMp3, _) = AudioValidation.Validate(4096, "audio/mpeg", "answer.mp3");
        Assert.True(validMp3);
    }

    [Fact]
    public async Task StartAttempt_when_insufficient_credits_throws_before_charging()
    {
        var certCode = Unique("SP01");
        var (_, examId, _, _) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 0);
        var (service, _, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<SpeakingPracticeException>(() => service.StartAttemptAsync(userId, examId));
        Assert.Equal(SpeakingPracticeException.InsufficientCredits, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attemptsCount = await db.ExamAttempts.CountAsync(a => a.UserId == userId);
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task StartAttempt_creates_in_progress_attempt_and_drafts_without_charging_credits()
    {
        var certCode = Unique("SP02");
        var (_, examId, _, _) = await SeedSpeakingExamFixtureAsync(certCode);
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
    public async Task SaveAudio_validates_type_and_size_and_persists_outside_web_root()
    {
        var certCode = Unique("SP03");
        var (_, examId, q1Id, _) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, fileStore, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);

        // Test invalid audio upload
        using var invalidStream = new MemoryStream(new byte[100]);
        var exInvalid = await Assert.ThrowsAsync<SpeakingPracticeException>(() =>
            service.SaveAudioAsync(userId, info.AttemptId, q1Id, invalidStream, "text/plain", "file.txt", 1000));
        Assert.Equal(SpeakingPracticeException.InvalidAudio, exInvalid.Code);

        // Test valid audio upload
        var fakeAudioBytes = Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....data....fake audio payload");
        using var validAudioStream = new MemoryStream(fakeAudioBytes);
        var saveResult = await service.SaveAudioAsync(userId, info.AttemptId, q1Id, validAudioStream, "audio/webm", "q1_take1.webm", 27000);

        Assert.Equal(q1Id, saveResult.QuestionId);
        Assert.Equal(27000, saveResult.DurationMs);
        Assert.False(string.IsNullOrEmpty(saveResult.AudioPath));

        // Check storage location is inside fileStore base directory (outside web root)
        var fileInfo = await fileStore.GetAudioAsync(saveResult.AudioPath);
        Assert.NotNull(fileInfo);
        Assert.Equal("audio/webm", fileInfo.ContentType);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var draft = await db.FreeResponses.SingleAsync(fr => fr.AttemptId == info.AttemptId && fr.QuestionId == q1Id);
        Assert.Equal(saveResult.AudioPath, draft.AudioPath);
        Assert.Equal(27000, draft.DurationMs);
        Assert.Equal("draft", draft.Status);
        Assert.Null(draft.SubmittedAt);
        Assert.NotNull(draft.DraftSavedAt);
    }

    [Fact]
    public async Task Audio_is_served_only_through_ownership_checked_endpoint()
    {
        var certCode = Unique("SP04");
        var (_, examId, q1Id, _) = await SeedSpeakingExamFixtureAsync(certCode);
        var ownerUserId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var otherUserId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(ownerUserId, examId);

        var audioBytes = Encoding.UTF8.GetBytes("fake audio recording content");
        using var stream = new MemoryStream(audioBytes);
        await service.SaveAudioAsync(ownerUserId, info.AttemptId, q1Id, stream, "audio/webm", "recording.webm", 15000);

        // Owner can access audio
        var ownerResult = await service.GetAudioFileAsync(ownerUserId, info.AttemptId, q1Id);
        Assert.NotNull(ownerResult);
        using (var reader = new StreamReader(ownerResult.Stream))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Equal("fake audio recording content", content);
        }

        // Non-owner is blocked with Forbidden
        var exForbidden = await Assert.ThrowsAsync<SpeakingPracticeException>(() =>
            service.GetAudioFileAsync(otherUserId, info.AttemptId, q1Id));
        Assert.Equal(SpeakingPracticeException.Forbidden, exForbidden.Code);
    }

    [Fact]
    public async Task SubmitAttempt_transitions_drafts_charges_one_credit_and_queues_ai_grading()
    {
        var certCode = Unique("SP05");
        var (_, examId, q1Id, q2Id) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("audio 1"));
        await service.SaveAudioAsync(userId, info.AttemptId, q1Id, stream1, "audio/webm", "q1.webm", 25000);

        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("audio 2"));
        await service.SaveAudioAsync(userId, info.AttemptId, q2Id, stream2, "audio/webm", "q2.webm", 30000);

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
    public async Task ProcessPendingGradings_generates_transcript_scores_criteria_and_marks_attempt_graded()
    {
        var certCode = Unique("SP06");
        var (_, examId, q1Id, q2Id) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        using var s1 = new MemoryStream(Encoding.UTF8.GetBytes("spoken answer 1"));
        await service.SaveAudioAsync(userId, info.AttemptId, q1Id, s1, "audio/webm", "q1.webm", 25000);
        using var s2 = new MemoryStream(Encoding.UTF8.GetBytes("spoken answer 2"));
        await service.SaveAudioAsync(userId, info.AttemptId, q2Id, s2, "audio/webm", "q2.webm", 30000);

        await service.SubmitAttemptAsync(userId, info.AttemptId);

        // Grading runs through the same pipeline
        var processed = await service.ProcessPendingGradingsAsync();
        Assert.True(processed >= 2);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("graded", attempt.Status);

        // Invariant: TotalScore and BandCode MUST remain NULL for speaking_practice
        Assert.Null(attempt.TotalScore);
        Assert.Null(attempt.BandCode);

        // All free responses marked 'graded' via trigger, with transcript generated in ResponseText
        var responses = await db.FreeResponses.Where(r => r.AttemptId == info.AttemptId).ToListAsync();
        Assert.All(responses, r =>
        {
            Assert.Equal("graded", r.Status);
            Assert.False(string.IsNullOrWhiteSpace(r.ResponseText), "Transcript should be populated by grader");
        });

        // AiGradingScores created with pronunciation/intonation comments
        var scores = await db.AiGradingScores.Include(s => s.AiGrading).Where(s => s.AiGrading.FreeResponse.AttemptId == info.AttemptId).ToListAsync();
        Assert.NotEmpty(scores);
        Assert.All(scores, s =>
        {
            Assert.True(s.Score >= 0);
            Assert.False(string.IsNullOrWhiteSpace(s.Comment));
        });
    }

    [Fact]
    public async Task Failed_grading_can_be_retried_reusing_row_without_additional_charges()
    {
        var certCode = Unique("SP07");
        var (_, examId, q1Id, _) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        // Include simulate_fail marker in audio file name to trigger simulated failure
        using var s1 = new MemoryStream(Encoding.UTF8.GetBytes("audio [simulate_fail] payload"));
        await service.SaveAudioAsync(userId, info.AttemptId, q1Id, s1, "audio/webm", "take_[simulate_fail].webm", 20000);
        await service.SubmitAttemptAsync(userId, info.AttemptId);

        await service.ProcessPendingGradingsAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var failedGrading = await db.AiGradings.Include(g => g.FreeResponse).SingleAsync(g => g.FreeResponse.AttemptId == info.AttemptId && g.FreeResponse.QuestionId == q1Id);
        Assert.Equal("failed", failedGrading.Status);
        var initialGradingId = failedGrading.GradingId;
        var initialCount = failedGrading.AttemptCount;

        // Check pending view
        var pendingView = await service.GetGradingPendingAsync(userId, info.AttemptId);
        Assert.True(pendingView.CanRetry);
        Assert.Equal(1, pendingView.FailedResponses);

        // Retry grading
        await service.RetryGradingAsync(userId, info.AttemptId);

        await using var dbAfter = DbContextFactory.Create(database.ConnectionString);
        var retriedGrading = await dbAfter.AiGradings.SingleAsync(g => g.GradingId == initialGradingId);
        Assert.Equal("pending", retriedGrading.Status);
        Assert.Equal(initialCount + 1, retriedGrading.AttemptCount);
        Assert.Null(retriedGrading.FailureReason);

        // Verify credit balance not debited again
        var balance = await dbAfter.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(1, balance);
    }

    [Fact]
    public async Task Speaking_review_renders_criterion_scores_transcript_and_pronunciation_feedback()
    {
        var certCode = Unique("SP08");
        var (_, examId, q1Id, q2Id) = await SeedSpeakingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _, _, _) = CreateServices();

        var info = await service.StartAttemptAsync(userId, examId);
        using var s1 = new MemoryStream(Encoding.UTF8.GetBytes("spoken answer 1"));
        await service.SaveAudioAsync(userId, info.AttemptId, q1Id, s1, "audio/webm", "q1.webm", 25000);
        using var s2 = new MemoryStream(Encoding.UTF8.GetBytes("spoken answer 2"));
        await service.SaveAudioAsync(userId, info.AttemptId, q2Id, s2, "audio/webm", "q2.webm", 30000);

        await service.SubmitAttemptAsync(userId, info.AttemptId);
        await service.ProcessPendingGradingsAsync();

        var review = await service.GetAttemptReviewAsync(userId, info.AttemptId);

        Assert.Equal(info.AttemptId, review.AttemptId);
        Assert.Equal(2, review.TotalQuestions);
        Assert.Equal(2, review.AnsweredQuestions);
        Assert.True(review.TotalScore > 0);
        Assert.True(review.MaxScore > 0);
        Assert.NotNull(review.ScaledScore);
        Assert.Equal(2, review.Questions.Count);

        // Check criteria scores, pronunciation feedback and transcripts
        foreach (var q in review.Questions)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Transcript), "Each question in review should have a transcript");
            Assert.False(string.IsNullOrWhiteSpace(q.AudioPath), "Each question should have an audio path");
            Assert.NotEmpty(q.Criteria);
            Assert.All(q.Criteria, c =>
            {
                Assert.True(c.Score <= c.MaxScore);
                Assert.False(string.IsNullOrWhiteSpace(c.Comment), "Pronunciation/rubric feedback comment should be present");
            });
        }

        // Lowest scored question identified
        Assert.NotNull(review.LowestScoredQuestion);
    }
}
