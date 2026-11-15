using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #361: what the admin upload path writes is what a learner's drill draws, and the publish gate did
/// not loosen to let that stock in.
/// <para>
/// <b>Why this file exists.</b> Practice was already wired - eight client calls to eight routes, a
/// payload builder that agrees with <c>StartPracticeRequest</c> - so the open question was content, not
/// plumbing. Every position below is authored by the same service the admin upload reaches
/// (<see cref="IAdminItemService.CreateAsync"/> + <see cref="AdminExamService.BindItemAsync"/>), never
/// by a fixture writing <c>Items</c>/<c>ExamItems</c> directly, and every assertion is made at the seam
/// the browser calls.
/// </para>
/// <para>
/// <b>What is deliberately not duplicated here.</b> "An exam with a finished attempt refuses a
/// structural change" is <c>AdminExamPublishGateTests.An_exam_with_a_finished_attempt_refuses_structural_edits_and_names_the_count</c>
/// and <c>...Quick_ingest_cannot_add_questions_to_an_exam_with_a_finished_attempt</c>; both were
/// re-run alongside this file rather than copied. "Publishing an empty paper names its audit codes" is
/// <c>...Publishing_an_empty_exam_names_the_failed_audit_codes</c>. The gate test below therefore
/// asserts the half that existed nowhere: the audio deficit clearing while the question deficit
/// remains.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminContentPipelineTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AdminExamService CreateExamService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private PracticeService CreatePractice() =>
        new(DbContextFactory.Create(database.ConnectionString), new LearnerService(DbContextFactory.Create(database.ConnectionString)));

    /// <summary>
    /// The pool's topic vocabulary is a closed set of twenty that no certificate owns, so a fixture
    /// authors the two it needs and every test shares them. Guarded, because the class shares one
    /// catalog with the rest of the suite.
    /// </summary>
    // MERGE, not a plain INSERT: this class is a [Collection(DatabaseCollection.Name)] member, so both
    // tests share one seeded catalog rather than getting a fresh database each. A second INSERT then
    // hits UQ_Topics_Modality_Code - proved by reverting to a plain INSERT, which fails 1 of 2 with a
    // duplicate key. Idempotence is the whole point of the MERGE.
    private Task EnsureTopicsAsync() => database.SqlAsync("""
        MERGE Topics AS t
        USING (VALUES
          ('listening','L-DET',N'Listening for detail'),
          ('reading','R-DET',N'Reading for detail')
        ) v(Modality, Code, Name)
        ON t.Modality = v.Modality AND t.Code = v.Code
        WHEN NOT MATCHED THEN INSERT (Modality, Code, Name) VALUES (v.Modality, v.Code, v.Name);
        """);

    /// <summary>
    /// A certificate holding one readable section that declares the two positions the test fills. The
    /// paper is a draft on purpose: this test is about what a drill draws, not about publishing.
    /// </summary>
    private async Task<(int CertId, int SectionId, string CertCode)> SeedReadingCertificateAsync()
    {
        await EnsureTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var certCode = Unique("PIPE");
        var cert = new Certificate { Code = certCode, Name = $"Pipeline {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1,
            DurationMinutes = 60,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5",
            Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 2,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        return (cert.CertId, section.SectionId, certCode);
    }

    /// <summary>
    /// A certificate holding one listening section that declares a single question. One question
    /// declared, so "short of its count" and "no audio yet" are the only two deficits the gate can
    /// report, and filling one leaves the other readable.
    /// </summary>
    private async Task<(int CertId, int SectionId)> SeedListeningCertificateAsync()
    {
        await EnsureTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var certCode = Unique("AUDIO");
        var cert = new Certificate { Code = certCode, Name = $"Audio {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1,
            // INV-5: the served skill's duration is the paper's, or publish is refused for a second,
            // unrelated reason and the assertions below stop being about the audio.
            DurationMinutes = 60,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = "listening", Code = "L1",
            Name = "Listening Part 1", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        return (cert.CertId, section.SectionId);
    }

    private async Task<int> TopicIdAsync(string modality, string code)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Topics.Where(t => t.Modality == modality && t.Code == code)
            .Select(t => t.TopicId).SingleAsync();
    }

    /// <summary>
    /// A learner whose active certificate is the one under test, with a completed path step covering
    /// the skill - which is what unlocks free practice (#313). Mirrors <c>PracticePoolTests</c>.
    /// </summary>
    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            DECLARE @certId INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Pipeline Learner', @certId);
            DECLARE @u INT = SCOPE_IDENTITY();
            DECLARE @band NVARCHAR(20) = COALESCE((SELECT TOP 1 Code FROM ScoreBands WHERE CertId = @certId ORDER BY DisplayOrder), 'B1');
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status, StartedAt, WindowStartDate)
            VALUES (@u, @certId, @band, 'active', SYSDATETIMEOFFSET(), CAST(SYSDATETIMEOFFSET() AS DATE));
            DECLARE @lp INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps
              (LearnerPathId, CertId, WeekNumber, WeekSequence, StepType, Status, SkillId, Modality, TopicId, TargetCefr, SizingTier, PlannedItemCount, EstimatedMinutes, TargetAccuracyPct, CompletedAt)
            SELECT @lp, @certId, 1, ROW_NUMBER() OVER (ORDER BY s.SkillId), 'focus_practice', 'done', s.SkillId, s.Modality, t.TopicId, 'B1', 'developing', 1, 10, 70, SYSDATETIMEOFFSET()
            FROM Skills s
            CROSS APPLY (SELECT TOP 1 TopicId FROM Topics tp WHERE tp.Modality = s.Modality ORDER BY tp.TopicId) t
            WHERE s.CertId = @certId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// A four-option reading item as the admin's pool editor writes one. Four near-equal-length
    /// options and a key of its own, so gate one's shape rules hold and the key is unambiguous.
    /// </summary>
    private static CreatePoolItemRequest ReadingItem(int topicId, string stem) => new(
        ModalityDto.Reading,
        topicId,
        "B1",
        "sentence_cloze",
        stem,
        OptionA: "The depot opens at six in the morning.",
        OptionB: "The ledger is signed after the gate closes.",
        OptionC: "Visitors wait outside until the supervisor arrives.",
        OptionD: "Only the supervisor may open the loading bay.",
        KeyLabel: "A",
        Explanation: "The passage states the opening time.");

    private static CreatePoolItemRequest ListeningItem(int topicId, string stem) => new(
        ModalityDto.Listening,
        topicId,
        "B1",
        "short_exchange",
        stem,
        OptionA: "The train leaves from platform four.",
        OptionB: "The train is delayed until the evening.",
        OptionC: "The station closes before the train arrives.",
        OptionD: "The timetable moves to another board.",
        KeyLabel: "A",
        Explanation: "The announcement names platform four.");

    /// <summary>
    /// AC1 and AC2. Two positions authored through the two acts the admin upload performs - create an
    /// item, bind its version at a position - and a drill against that section returns them, grades an
    /// answer against the version's key, and produces a summary with an insight.
    /// <para>
    /// Nothing here writes <c>Items</c>, <c>ItemVersions</c> or <c>ExamItems</c> by hand: a drill that
    /// draws what the upload wrote is exactly the claim, so a fixture that wrote the rows itself would
    /// prove half of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Practice_draws_stock_written_through_the_admin_path_and_grades_an_answer()
    {
        var (certId, sectionId, certCode) = await SeedReadingCertificateAsync();
        var examService = CreateExamService();
        var exam = await examService.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Pipeline {Guid.NewGuid():N}"[..28], 60, ExamPurposeDto.Mock, QuestionCount: 2));

        var topicId = await TopicIdAsync("reading", "R-DET");
        PoolItemDto first;
        PoolItemDto second;
        await using (var adminDb = DbContextFactory.Create(database.ConnectionString))
        {
            var items = new AdminItemService(adminDb, TimeProvider.System);
            first = await items.CreateAsync(ReadingItem(topicId, "The depot opens at six."));
            second = await items.CreateAsync(ReadingItem(topicId, "The ledger is signed after the gate closes."));
        }

        await examService.BindItemAsync(exam.ExamId, new BindPoolItemRequest(sectionId, first.ItemVersionId, 1));
        await examService.BindItemAsync(exam.ExamId, new BindPoolItemRequest(sectionId, second.ItemVersionId, 2));

        var userId = await SeedLearnerAsync(certCode);
        var practice = CreatePractice();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledgerBefore = await db.CreditTransactions.CountAsync(t => t.UserId == userId);

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));

        Assert.Equal(sectionId, session.SectionId);
        Assert.Equal(2, session.QuestionCount);
        Assert.Equal(PracticeStatus.InProgress, session.Status);

        var take = await practice.GetSessionTakeAsync(userId, session.SessionId);
        // A pool position: no question row, addressed by the version it pinned.
        Assert.Null(take.Question.QuestionId);
        Assert.Equal(first.ItemVersionId, take.Question.ItemVersionId);
        Assert.Equal("The depot opens at six.", take.Question.Stem);
        Assert.Equal(4, take.Question.Options.Count);
        Assert.Null(take.Question.CorrectOptionId);

        // The key is 'A' on the version the admin wrote, and the id is the projection's own.
        var key = take.Question.Options.Single(o => o.Label == "A").OptionId;
        var graded = await practice.SubmitAnswerAsync(
            userId, session.SessionId, first.ItemVersionId, new SubmitPracticeAnswerInput(key));
        Assert.True(graded.IsCorrect);

        var secondQuestion = (await practice.GetSessionTakeAsync(userId, session.SessionId, displayOrder: 2)).Question;
        Assert.Equal(second.ItemVersionId, secondQuestion.ItemVersionId);
        await practice.SubmitAnswerAsync(
            userId, session.SessionId, second.ItemVersionId,
            new SubmitPracticeAnswerInput(secondQuestion.Options.Single(o => o.Label == "A").OptionId));

        var info = await practice.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.Finished, info.Status);
        Assert.Equal(2, info.CorrectCount);

        var summary = await practice.GetSummaryAsync(userId, session.SessionId);
        Assert.Equal(2, summary.TotalQuestions);
        Assert.Equal(2, summary.AnsweredCount);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(100, summary.AccuracyPercentage);
        Assert.All(summary.Questions, q =>
        {
            Assert.True(q.IsCorrect);
            Assert.Equal("A", q.CorrectOptionLabel);
        });

        // The result page's insight, over the session the admin's stock produced.
        var insight = await new AiInsightService(
                DbContextFactory.Create(database.ConnectionString),
                new FakeAiInsightGenerator(),
                NullLogger<AiInsightService>.Instance)
            .GetOrCreatePracticeInsightAsync(userId, session.SessionId);
        Assert.Equal(AiInsightScope.PracticeSession, insight.Scope);
        Assert.Equal(session.SessionId, insight.PracticeSessionId);

        // AC2: a drill is free. Practice never charges, so the ledger is the same length after a
        // started, answered and finished session as before it - and its balance is still nil.
        Assert.Equal(0, ledgerBefore);
        Assert.Equal(ledgerBefore, await db.CreditTransactions.CountAsync(t => t.UserId == userId));
        Assert.Equal(0, await db.CreditTransactions.Where(t => t.UserId == userId).SumAsync(t => (int?)t.Delta) ?? 0);
    }

    /// <summary>
    /// AC4 and AC5, and the upload half of the gate. A listening paper that holds nothing and has no
    /// audio track is refused naming both deficits; uploading the section's audio through the admin's
    /// own route clears the audio deficit and <em>only</em> that one, so the paper stays refused until
    /// its declared position is filled. Then the same paper publishes.
    /// <para>
    /// This is the assertion <c>AdminExamPublishGateTests</c> does not make: it proves the gate refuses
    /// an empty paper, but not that an upload clears one named deficit while leaving the other.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Saving_section_audio_clears_the_audio_deficit_while_a_short_paper_stays_refused()
    {
        var (certId, sectionId) = await SeedListeningCertificateAsync();
        var examService = CreateExamService();
        var exam = await examService.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Audio {Guid.NewGuid():N}"[..28], 60, ExamPurposeDto.Mock));

        var beforeAudio = await examService.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.False(beforeAudio.CanPublish);
        Assert.Contains(beforeAudio.Issues, i => i.Code == "SEC_AUDIO_MISSING");
        Assert.Contains(beforeAudio.Issues, i => i.Code == "SEC_QUESTION_COUNT_LOW");

        var refusedBefore = await Assert.ThrowsAsync<AdminExamException>(() => examService.PublishExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, refusedBefore.Code);
        Assert.Contains("SEC_AUDIO_MISSING", refusedBefore.Message, StringComparison.Ordinal);
        Assert.Contains("SEC_QUESTION_COUNT_LOW", refusedBefore.Message, StringComparison.Ordinal);

        // The admin's media upload, at the seam the WebAPI route resolves.
        await examService.SaveSectionAudioAsync(exam.ExamId, new SaveExamSectionAudioRequest(
            sectionId, "exams/pipeline/listening.mp3", DurationSec: 95, SizeBytes: 1024));

        var afterAudio = await examService.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(afterAudio.Issues, i => i.Code == "SEC_AUDIO_MISSING");
        Assert.Contains(afterAudio.Issues, i => i.Code == "SEC_QUESTION_COUNT_LOW");
        Assert.False(afterAudio.CanPublish);

        var refusedAfter = await Assert.ThrowsAsync<AdminExamException>(() => examService.PublishExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, refusedAfter.Code);
        Assert.DoesNotContain("SEC_AUDIO_MISSING", refusedAfter.Message, StringComparison.Ordinal);
        Assert.Contains("SEC_QUESTION_COUNT_LOW", refusedAfter.Message, StringComparison.Ordinal);

        // The other half of the upload: filling the declared position is how the remaining deficit
        // clears, and the same paper then publishes.
        var topicId = await TopicIdAsync("listening", "L-DET");
        await using (var adminDb = DbContextFactory.Create(database.ConnectionString))
        {
            var created = await new AdminItemService(adminDb, TimeProvider.System)
                .CreateAsync(ListeningItem(topicId, "Where does the train leave from?"));
            await examService.BindItemAsync(exam.ExamId, new BindPoolItemRequest(sectionId, created.ItemVersionId, 1));
        }

        var published = await examService.PublishExamAsync(exam.ExamId);
        Assert.True(published.Success);
        Assert.Equal(ExamStatusDto.Published, (await examService.GetExamByIdAsync(exam.ExamId)).Status);
    }
}
