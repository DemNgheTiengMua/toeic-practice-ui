using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
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

    /// <summary>
    /// What it answers for normal free speech: every figure a percentage, which is what a live
    /// speak.eval.pro 200 actually returns (#152, confirmed against the real account).
    /// </summary>
    private static readonly SpeechSuperAssessmentResult HeardFine = new(
        Success: true, ErrorMessage: null, ErrId: null,
        Transcription: "Attention all shoppers the sale ends today",
        OverallScore: 85m, PronunciationScore: 85m, FluencyScore: 80m,
        GrammarScore: 85m, VocabularyScore: 85m, RelevanceScore: 94m);

    /// <summary>
    /// One speaking certificate with a Read Aloud question (script authored) and a Describe a Picture
    /// question (a picture, no description). <paramref name="skillScale"/> is the skill's own scale;
    /// <paramref name="bands"/> is the certificate's band table - the two places the reported axis may
    /// come from.
    /// </summary>
    private async Task<(int CertId, int ExamId, int ScriptQuestionId, int PictureQuestionId)> SeedAsync(
        string certCode, (int Min, int Max, int Step)? skillScale, params (string Code, int Min, int Max)[] bands) =>
        await SeedPaperAsync(certCode, "sum", skillScale, bands);

    private async Task<(int CertId, int ExamId, int ScriptQuestionId, int PictureQuestionId)> SeedAsync(
        string certCode, string scoreAggregationRule, (int Min, int Max, int Step)? skillScale, params (string Code, int Min, int Max)[] bands) =>
        await SeedPaperAsync(certCode, scoreAggregationRule, skillScale, bands);

    /// <summary>
    /// Two speaking tasks on one practice paper: a scripted read-aloud and a picture description.
    /// <para>
    /// #272: both are pool items. The script is a <c>ItemVersion</c> fact (ReferenceText/ReferenceKind);
    /// the picture is a <c>Stimulus</c> fact, because on the pool an image belongs to the passage/context
    /// block its positions share rather than to a question-group row.
    /// </para>
    /// </summary>
    private async Task<(int CertId, int ExamId, int ScriptQuestionId, int PictureQuestionId)> SeedPaperAsync(
        string certCode, string scoreAggregationRule, (int Min, int Max, int Step)? skillScale,
        (string Code, int Min, int Max)[] bands,
        string? pictureReferenceText = null, string? pictureReferenceKind = null)
    {
        var scale = skillScale is (int smin, int smax, int sstep) ? $"{smin}, {smax}, {sstep}" : "NULL, NULL, NULL";
        var bandSql = bands.Length == 0
            ? ""
            : "INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES "
              + string.Join(", ", bands.Select((b, i) => $"(@cert, '{b.Code}', N'{b.Code}', {b.Min}, {b.Max}, {i})")) + ";";

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule) VALUES ('{certCode}', N'Cert {certCode}', 1, '{scoreAggregationRule}');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'SPEA', N'Speaking', 'speaking', 1, 20, {scale});
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'SPEA');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Read aloud', 1, 1),
              (@sS, @cert, 'speaking', 'S2', N'Describe picture', 1, 2);

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sS, 'PRON', N'Pronunciation', 3.0, 1.0, 1),
              (@cert, @sS, 'REL',  N'Relevance and completeness', 3.0, 1.0, 2);

            {bandSql}

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Speaking Practice {certCode}', 'published', 20, 'speaking_practice', 2);
            """);

        // #272: the two tasks are pool items. A task's reference is a fact about its VERSION
        // (ReferenceText/ReferenceKind), and the picture belongs to the STIMULUS the second position
        // shares - which is the same split the group/question columns used to carry.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section1 = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "S1");
        var section2 = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "S2");

        var scripted = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section1.SectionId, "speaking", "Read the announcement aloud.",
            displayOrder: 1, level: "A2",
            referenceText: "Attention all shoppers. The sale ends today at 9:00 PM.",
            referenceKind: "script");
        var picture = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section2.SectionId, "speaking", "Describe what you see in the picture.",
            displayOrder: 1, level: "B1",
            referenceText: pictureReferenceText, referenceKind: pictureReferenceKind);

        await database.SqlAsync($"""
            INSERT INTO Stimuli (CefrLevel, DomainContext, ImagePath, ImageAlt, DisplayOrder)
            VALUES ('B1', 'general', '/img/sample.jpg', N'Office setting', 1);
            UPDATE Items SET StimulusId = SCOPE_IDENTITY() WHERE ItemId = {picture.ItemId};
            """);

        return (cert.CertId, exam.ExamId, scripted.ExamItemId, picture.ExamItemId);
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
        // One audio, one provider answer: pron 85% and relevance 94% of two 3.0 criteria.
        //   pron -> 2.6, relevance -> 2.8, so the rubric is 5.4 of 6.0 = 90%.
        // Only the certificate's own data decides where that 90% lands - the old constant put every
        // one of these on 0-200.
        var cases = new (string Prefix, string Rule, (int, int, int)? SkillScale, (string, int, int)[] Bands, int Expected, int ExpectedMax, int Factor, string Why)[]
        {
            ("FIF", "sum", null, [("B1", 0, 25), ("B2", 30, 50)], 45, 50, 1,
                "90% of a 0-50 band span, on the 5-point grid its own bands sit on"),
            // A 0-9 certificate stored in tenths with 0.5 steps, as IELTS is: 90% of 90 is 81, snapped
            // to the nearest half band. The stored 80 is 8.0, and the factor says so.
            ("HALF", "avg_half_step", null, [("B1", 30, 50), ("B2", 55, 65), ("C1", 70, 80), ("C2", 85, 90)], 80, 90, 10,
                "90% of 0-9 expressed in tenths, snapped to 0.5"),
            // The TOEIC_SW shape: 50 is where that scale genuinely starts, and 90% of it is 190.
            // #171: this certificate used to declare the removed `independent` rule; the scale, the
            // bands and the reported score are the same under `sum` because a skill-practice attempt
            // scores one skill and never carries a certificate total.
            ("SW", "sum", (50, 200, 10), [("SW_B1", 160, 250), ("SW_B2", 260, 400)], 190, 200, 1,
                "90% across a configured 50-200 scale, snapped to 10")
        };

        foreach (var (prefix, rule, skillScale, bands, expected, expectedMax, factor, why) in cases)
        {
            var certCode = Unique(prefix);
            var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, rule, skillScale, bands);
            var userId = await SeedLearnerAsync(certCode);

            var store = TempStore();
            var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
            await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

            var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);
            Assert.True(review.ScaledScore.HasValue, "no reported score for " + certCode);
            Assert.Equal(expected, review.ScaledScore.Value);
            Assert.Equal(expectedMax, review.ScaledScoreMax);
            Assert.InRange(review.ScaledScore.Value, 0, expectedMax);
            Assert.Equal(factor, review.ScoreDisplayFactor);
            _ = why;
        }
    }

    /// <summary>
    /// #152 AC3, and the defect the review found: a 0-9 certificate stored 80 for a 7.5 and the page
    /// printed "80 · out of 90 · IELTS scale". The stored int is fine; what was missing is the unit.
    /// This crosses service → DTO → the text a learner reads, through the same formatter the page
    /// calls, so a units mistake cannot pass: the assertions are on the rendered strings.
    /// </summary>
    [Fact]
    public async Task A_tenths_certificate_reports_the_score_the_learner_reads_and_never_the_stored_integer()
    {
        var certCode = Unique("TENTH");
        var (_, examId, scriptQuestionId, _) = await SeedAsync(
            certCode, "avg_half_step", null,
            ("B1", 30, 50), ("B2", 55, 65), ("C1", 70, 80), ("C2", 85, 90));
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

        var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);

        // Stored: 80 on a 0-90 axis, because the certificate keeps tenths.
        Assert.Equal(80, review.ScaledScore);
        Assert.Equal(90, review.ScaledScoreMax);
        Assert.Equal(10, review.ScoreDisplayFactor);

        // Read: 8 out of 9. The stored 80 must never reach the learner as a score.
        Assert.Equal("8", ScoreDisplay.Format(review.ScaledScore!.Value, review.ScoreDisplayFactor));
        Assert.Equal("9", ScoreDisplay.Format(review.ScaledScoreMax!.Value, review.ScoreDisplayFactor));
        Assert.NotEqual("80", ScoreDisplay.Format(review.ScaledScore!.Value, review.ScoreDisplayFactor));
    }

    /// <summary>
    /// The same chain on a certificate that reports whole points: nothing is divided, and the number
    /// the learner reads is the number stored.
    /// </summary>
    [Fact]
    public async Task A_whole_point_certificate_reports_the_stored_number_unchanged()
    {
        var certCode = Unique("WHOLE");
        var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, "sum", null, ("B1", 0, 25), ("B2", 30, 50));
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

        var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);

        Assert.Equal(1, review.ScoreDisplayFactor);
        Assert.Equal("45", ScoreDisplay.Format(review.ScaledScore!.Value, review.ScoreDisplayFactor));
        Assert.Equal("50", ScoreDisplay.Format(review.ScaledScoreMax!.Value, review.ScoreDisplayFactor));
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

    /// <summary>
    /// #152 finding 4: a scripted question whose group happens to carry a logo or chart must keep its
    /// script. The mode is the question's own declared <c>ReferenceKind</c>, never the presence of an
    /// image on its group — a group-level fact cannot answer a question-level question.
    /// </summary>
    [Fact]
    public async Task A_scripted_question_on_a_group_with_a_picture_still_sends_its_script()
    {
        var certCode = Unique("LOGO");
        var (_, examId, scriptQuestionId, _) = await SeedAsync(certCode, null, ("B1", 0, 50));
        var userId = await SeedLearnerAsync(certCode);

        // Give the Read Aloud task's stimulus a picture — a logo, a chart, a photo. Nothing about the
        // task's own text changes: it is still the words to read. #272: a pool task shares its stimulus
        // with the other positions in its block, so this is the equivalent of the old group image.
        await database.SqlAsync($"""
            INSERT INTO Stimuli (CefrLevel, DomainContext, ImagePath, ImageAlt, DisplayOrder)
            VALUES ('A2', 'general', '/img/logo.svg', N'Company logo', 1);
            UPDATE Items SET StimulusId = SCOPE_IDENTITY()
            WHERE ItemId = (SELECT ItemId FROM ExamItems WHERE ExamItemId = {scriptQuestionId});
            """);

        var store = TempStore();
        var provider = new StubProvider(HeardFine);
        var service = Service(store);
        var attempt = await service.StartAttemptAsync(userId, examId);
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....data....silence")))
        {
            await service.SaveAudioAsync(userId, attempt.AttemptId, scriptQuestionId, stream, "audio/wav", "silent.wav", 1000);
        }

        // The take page shows the words to read, and the reference is not lost to the picture.
        var take = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, null);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", take.Question.ReadAloudText);

        await service.SubmitAttemptAsync(userId, attempt.AttemptId);
        await ProcessAsync(new SpeechSuperAiGrader(provider, store), database);

        var call = Assert.Single(provider.Calls);
        Assert.Equal("Attention all shoppers. The sale ends today at 9:00 PM.", call.RefText);
        Assert.Equal("Read the announcement aloud.", call.Prompt);
    }

    /// <summary>
    /// #152 finding 4, the other direction: a description of the picture is context for relevance,
    /// not something to read. Its stimulus's picture is what makes it a description — but the *kind*
    /// still comes from the task's own version, because an image cannot describe itself.
    /// <para>
    /// #272: the reference is authored with the version, because <c>trg_ItemVersions_ScoredFieldsImmutable</c>
    /// refuses an in-place move of it - the copy-on-write rule (CNT-43).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_picture_description_is_prompt_context_and_never_a_pronunciation_reference()
    {
        var certCode = Unique("DESC");
        var (_, examId, _, pictureQuestionId) = await SeedPaperAsync(
            certCode, "sum", null, [("B1", 0, 50)],
            pictureReferenceText: "Two colleagues are talking across a desk.", pictureReferenceKind: "image_desc");
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var provider = new StubProvider(HeardFine);
        var service = Service(store);
        var attempt = await service.StartAttemptAsync(userId, examId);
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....data....silence")))
        {
            await service.SaveAudioAsync(userId, attempt.AttemptId, pictureQuestionId, stream, "audio/wav", "silent.wav", 1000);
        }

        // The take page shows the picture, not a script: a description is context for relevance.
        var take = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, pictureQuestionId);
        Assert.Null(take.Question.ReadAloudText);

        await service.SubmitAttemptAsync(userId, attempt.AttemptId);
        await ProcessAsync(new SpeechSuperAiGrader(provider, store), database);

        var call = Assert.Single(provider.Calls);
        Assert.Equal("", call.RefText);
        Assert.Contains("Two colleagues are talking across a desk.", call.Prompt);
        Assert.Contains("Describe what you see in the picture.", call.Prompt);
    }

    /// <summary>
    /// #152 finding 3: the score and the band come from one axis. Here they are both derived from the
    /// same rubric fraction on the same certificate, so a band that the reported score cannot reach
    /// is impossible by construction.
    /// </summary>
    [Fact]
    public async Task The_reported_score_and_the_reported_band_land_on_the_same_axis()
    {
        var certCode = Unique("AXIS");
        var (certId, examId, scriptQuestionId, _) = await SeedAsync(
            certCode, "avg_half_step", null,
            ("B1", 40, 50), ("B2", 55, 65), ("C1", 70, 80), ("C2", 85, 90));
        var userId = await SeedLearnerAsync(certCode);

        var store = TempStore();
        var attemptId = await RecordAndSubmitAsync(Service(store), userId, examId, scriptQuestionId);
        await ProcessAsync(new SpeechSuperAiGrader(new StubProvider(HeardFine), store), database);

        var review = await Service(store).GetAttemptReviewAsync(userId, attemptId);

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var bands = await check.ScoreBands.Where(b => b.CertId == certId).OrderBy(b => b.MinTotal).ToListAsync();
        var band = Assert.Single(bands, b => b.Code == review.EstimatedBandCode);

        // 90% of a 0-90 axis is 80, which is inside C1 (70-80) — and the page says 8 of 9.
        Assert.Equal(80, review.ScaledScore);
        Assert.Equal("C1", review.EstimatedBandCode);
        Assert.InRange(review.ScaledScore!.Value, band.MinTotal, band.MaxTotal);
        Assert.Equal("8", ScoreDisplay.Format(review.ScaledScore!.Value, review.ScoreDisplayFactor));
    }
}
