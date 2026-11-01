using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #152: what the grading path does when the provider answers with nothing, and where the review page
/// puts the score once it does answer. These run against the real schema (real triggers, real
/// <c>AiGradings</c> status transitions), because the defect being removed was a number appearing in a
/// column that should have stayed empty.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SpeakingGradingScaleTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private sealed class StubGrader(Func<AiGradingRequest, AiGradingResult> answer) : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(answer(request));
    }

    /// <summary>The real client contract, answered without any network call.</summary>
    private sealed class StubProvider : ISpeechSuperClient
    {
        private readonly SpeechSuperAssessmentResult _answer;

        public StubProvider(SpeechSuperAssessmentResult answer) => _answer = answer;

        public List<(string Prompt, string RefText)> Calls { get; } = [];

        public Task<SpeechSuperAssessmentResult> AssessAsync(
            Stream audioStream, string audioType, string? questionPrompt = null, string? refText = null,
            string? userId = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((questionPrompt ?? "", refText ?? ""));
            return Task.FromResult(_answer);
        }
    }

    /// <summary>What <c>speak.eval.pro</c> answers for a clip it recognised nothing in.</summary>
    private static readonly SpeechSuperAssessmentResult HeardNothing = new(
        Success: true, ErrorMessage: null, ErrId: null, Transcription: "",
        OverallScore: 0, PronunciationScore: 0, FluencyScore: 0,
        GrammarScore: 0, VocabularyScore: 0, RelevanceScore: 0);

    /// <summary>What it answers for normal free speech: named scores on 0-9, relevance as a percentage.</summary>
    private static readonly SpeechSuperAssessmentResult HeardFine = new(
        Success: true, ErrorMessage: null, ErrId: null,
        Transcription: "Attention all shoppers the sale ends today",
        OverallScore: 7.2m, PronunciationScore: 7.5m, FluencyScore: 7.2m,
        GrammarScore: 7.2m, VocabularyScore: 7.2m, RelevanceScore: 95m);

    /// <summary>
    /// One speaking certificate with a Read Aloud question (script authored) and a Describe a Picture
    /// question (a picture, no description). <paramref name="skillScale"/> is the skill's own scale;
    /// <paramref name="bands"/> is the certificate's band table - the two places the reported axis may
    /// come from.
    /// </summary>
    private async Task<(int CertId, int ExamId, int ScriptQuestionId, int PictureQuestionId)> SeedAsync(
        string certCode, (int Min, int Max, int Step)? skillScale, params (string Code, int Min, int Max)[] bands)
    {
        var scale = skillScale is (int smin, int smax, int sstep) ? $"{smin}, {smax}, {sstep}" : "NULL, NULL, NULL";
        var bandSql = bands.Length == 0
            ? ""
            : "INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES "
              + string.Join(", ", bands.Select((b, i) => $"(@cert, '{b.Code}', N'{b.Code}', {b.Min}, {b.Max}, {i})")) + ";";

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule) VALUES ('{certCode}', N'Cert {certCode}', 1, 'sum');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'SPEA', N'Speaking', 'speaking', 1, 20, {scale});
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'SPEA');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Read aloud', 1, 1),
              (@sS, @cert, 'speaking', 'S2', N'Describe picture', 1, 2);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S1');
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S2');

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sS, 'PRON', N'Pronunciation', 3.0, 1.0, 1),
              (@cert, @sS, 'REL',  N'Relevance and completeness', 3.0, 1.0, 2);

            {bandSql}

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Speaking Practice {certCode}', 'published', 20, 'speaking_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder, SampleAnswerOrImageDesc)
            VALUES (@grp1, @exam, @sec1, @sS, N'Read the announcement aloud.', 1, 'free_response', 1,
                    N'Attention all shoppers. The sale ends today at 9:00 PM.');

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, ImagePath, ImageAlt)
            VALUES (@exam, @cert, @sec2, 2, '/img/sample.jpg', N'Office setting');
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sS, N'Describe what you see in the picture.', 2, 'free_response', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    private async Task<int> SeedLearnerAsync(string certCode, int credits = 3)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{adminEmail}', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @adminId INT = (SELECT UserId FROM Users WHERE Email = '{adminEmail}');
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ((SELECT UserId FROM Users WHERE Email = '{email}'), 'admin_grant', {credits}, @adminId, N'Test setup credits');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>A fresh service per call: one HTTP request gets one scope, and so does one call here.</summary>
    private SpeakingPracticeService Service(IFileStore store)
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new SpeakingPracticeService(db, new LearnerService(db), store);
    }

    private static LocalFileStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "ee_speaking_" + Guid.NewGuid().ToString("N")));

    private async Task<int> RecordAndSubmitAsync(
        SpeakingPracticeService service, int userId, int examId, params int[] questionIds)
    {
        var attempt = await service.StartAttemptAsync(userId, examId);
        foreach (var questionId in questionIds)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....data....silence"));
            await service.SaveAudioAsync(userId, attempt.AttemptId, questionId, stream, "audio/wav", "silent.wav", 1000);
        }

        await service.SubmitAttemptAsync(userId, attempt.AttemptId);
        return attempt.AttemptId;
    }

    private static Task<int> ProcessAsync(IAiGrader grader, DatabaseFixture database) =>
        new AiGradingProcessor(DbContextFactory.Create(database.ConnectionString), grader)
            .ProcessPendingGradingsAsync();

    [Fact]
    public async Task A_silent_recording_is_failed_with_no_score_and_the_learner_is_offered_a_real_retry()
    {
        var certCode = Unique("SIL");
        var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, (0, 50, 1), ("B1", 0, 50));
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var provider = new StubProvider(HeardNothing);
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(provider, store), database);

        // The provider was asked twice (the one retry the ticket allows) and still produced nothing.
        Assert.Equal(2, provider.Calls.Count);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var grading = await check.AiGradings
            .Include(g => g.FreeResponse)
            .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);

        Assert.Equal("failed", grading.Status);
        Assert.Contains("no speech", grading.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(grading.OverallScaled);
        Assert.False(await check.AiGradingScores.AnyAsync(s => s.GradingId == grading.GradingId));

        // The "grade again" control the learner sees is real: the pending page offers it, the attempt
        // is not pretending to be graded, and the retry re-queues the row without a second charge.
        var service = Service(store);
        var pending = await service.GetGradingPendingAsync(userId, attemptId);
        Assert.True(pending.CanRetry);
        Assert.Equal(1, pending.FailedResponses);
        Assert.Equal(0, pending.GradedResponses);
        Assert.Contains("no speech", pending.FailureReason, StringComparison.OrdinalIgnoreCase);

        var attempt = await check.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        Assert.NotEqual(AttemptStatus.Graded.ToCode(), attempt.Status);
        Assert.Null(attempt.TotalScore);

        await service.RetryGradingAsync(userId, attemptId);
        await using var afterRetry = DbContextFactory.Create(database.ConnectionString);
        var requeued = await afterRetry.AiGradings.SingleAsync(g => g.GradingId == grading.GradingId);
        Assert.Equal("pending", requeued.Status);
        Assert.Equal(2, requeued.AttemptCount);
        var balance = await afterRetry.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(2, balance); // one credit for the submission, nothing extra for the retry
    }

    [Fact]
    public async Task The_same_rubric_lands_inside_a_0_to_50_certificate_and_on_the_half_band_grid_of_a_0_to_9_one()
    {
        // One audio, one provider answer: pron 7.5/9 and relevance 95% of two 3.0 criteria.
        //   pron -> 2.5, relevance -> 2.9, so the rubric is 5.4 of 6.0 = 90%.
        // Only the certificate's own data decides where that 90% lands - the old constant put every
        // one of these on 0-200.
        var cases = new (string Prefix, (int, int, int)? SkillScale, (string, int, int)[] Bands, int Expected, int ExpectedMax, string Why)[]
        {
            ("FIF", null, [("B1", 0, 25), ("B2", 30, 50)], 45, 50,
                "90% of a 0-50 band span, on the 5-point grid its own bands sit on"),
            // A 0-9 certificate stored in tenths with 0.5 steps, as IELTS is: 90% of 90 is 81, snapped
            // to the nearest half band.
            ("HALF", null, [("B1", 30, 50), ("B2", 55, 65), ("C1", 70, 80), ("C2", 85, 90)], 80, 90,
                "90% of 0-9 expressed in tenths, snapped to 0.5"),
            // The TOEIC_SW shape: the skill's own configured scale wins over the certificate's bands,
            // and 50 is where that scale genuinely starts.
            ("SW", (50, 200, 10), [("SW_B1", 160, 250), ("SW_B2", 260, 400)], 190, 200,
                "90% across a configured 50-200 scale, snapped to 10")
        };

        foreach (var (prefix, skillScale, bands, expected, expectedMax, why) in cases)
        {
            var certCode = Unique(prefix);
            var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, skillScale, bands);
            var userId = await SeedLearnerAsync(certCode);

            var store = TempStore();
            var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
            await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

            var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);
            Assert.True(review.ScaledScore.HasValue, "no reported score for " + certCode);
            Assert.Equal(expected, review.ScaledScore.Value);
            Assert.Equal(expectedMax, review.ScaledScoreMax);
            Assert.InRange(review.ScaledScore.Value, 0, expectedMax);
            _ = why;
        }
    }

    [Fact]
    public async Task A_certificate_with_no_bands_reports_no_score_rather_than_an_invented_range()
    {
        var certCode = Unique("NOB");
        var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, null);
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

        var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);
        Assert.Null(review.ScaledScore);
        Assert.Null(review.ScaledScoreMax);
        // The rubric itself is still reported, and its band estimate is what has nothing to say.
        Assert.True(review.TotalScore > 0);
        Assert.True(review.MaxScore > 0);
    }

    [Fact]
    public async Task The_read_aloud_question_carries_its_script_and_the_picture_question_carries_no_reference()
    {
        var certCode = Unique("REF");
        var (_, examId, scriptQuestionId, pictureQuestionId) = await SeedAsync(certCode, null, ("B1", 0, 50));
        var userId = await SeedLearnerAsync(certCode, credits: 5);

        var store = TempStore();
        var provider = new StubProvider(HeardFine);
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId, pictureQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(provider, store), database);

        Assert.Equal(2, provider.Calls.Count);

        // A Read Aloud question sends the words to read, so pronunciation is measured against something
        // that actually exists.
        var scripted = Assert.Single(provider.Calls, c => c.RefText.Length > 0);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", scripted.RefText);
        Assert.Equal("Read the announcement aloud.", scripted.Prompt);

        // A picture question sends no refText and keeps its prompt as topic context only.
        var picture = Assert.Single(provider.Calls, c => c.RefText.Length == 0);
        Assert.Equal("Describe what you see in the picture.", picture.Prompt);

        // With no reference, relevance is dropped from the result rather than scored against nothing.
        var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);
        var pictureItem = Assert.Single(review.Questions, q => q.QuestionId == pictureQuestionId);
        Assert.DoesNotContain(pictureItem.Criteria, c => c.Code == "REL");
        Assert.Contains(pictureItem.Criteria, c => c.Code == "PRON");

        var scriptedItem = Assert.Single(review.Questions, q => q.QuestionId == scriptQuestionId);
        Assert.Contains(scriptedItem.Criteria, c => c.Code == "REL");
        Assert.Contains(scriptedItem.Criteria, c => c.Code == "PRON");
    }
}
