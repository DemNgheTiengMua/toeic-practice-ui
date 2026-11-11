using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PracticeServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (PracticeService Practice, LearnerService Learner) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var practiceService = new PracticeService(db, learnerService);
        return (practiceService, learnerService);
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

    /// <summary>
    /// #269: the pool's topic vocabulary is a closed set of twenty that no certificate owns, so a
    /// fixture authors the four it needs. Guarded, because every test in this class shares one catalog.
    /// </summary>
    private Task EnsureTopicsAsync() => database.SqlAsync("""
        INSERT INTO Topics (Modality, Code, Name)
        SELECT v.Modality, v.Code, v.Name
        FROM (VALUES
          ('listening','L-DET',N'Detail'),
          ('reading','R-DET',N'Local detail'),
          ('speaking','S-DESC',N'Description'),
          ('writing','W-CORR',N'Correspondence')
        ) v(Modality, Code, Name)
        WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);
        """);

    /// <summary>
    /// The pool shape of the old <c>SeedCertificateWithQuestionsAsync</c>: a reading section holding
    /// three keyed positions (keys A, B, B) and a speaking section holding one productive task.
    /// <para>
    /// The id a drill answer names is the <b>pinned version</b>, and the option ids come from the
    /// projection (<see cref="PoolFixture.OptionIdAsync"/>), never from a second formula.
    /// </para>
    /// </summary>
    private sealed record PooledCertificate(
        int CertId,
        int SectionId,
        int SpeakingSectionId,
        int ExamId,
        List<int> ItemIds,
        List<int> VersionIds,
        List<int> ExamItemIds,
        List<int> CorrectOptionIds,
        List<int> WrongOptionIds);

    private async Task<PooledCertificate> SeedPoolCertificateAsync(
        string certCode, string? firstHint = null, string? firstPassage = null)
    {
        await EnsureTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        var spkSkill = new Skill { CertId = cert.CertId, Code = "SPEA", Name = "Speaking", Modality = "speaking", DisplayOrder = 2 };
        db.Skills.AddRange(skill, spkSkill);
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        var spkSec = new Section { CertId = cert.CertId, SkillId = spkSkill.SkillId, Modality = "speaking", Code = "S1", Name = "Speaking Part 1", DisplayOrder = 1, ExpectedQuestionCount = 1 };
        db.Sections.AddRange(readSec, spkSec);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        string[] keys = ["A", "B", "B"];
        var itemIds = new List<int>();
        var versionIds = new List<int>();
        var examItemIds = new List<int>();

        for (var i = 0; i < keys.Length; i++)
        {
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, readSec.SectionId, "reading", $"Stem {i + 1}",
                displayOrder: i + 1, keyLabel: keys[i], level: "B2", explanation: $"Explanation {i + 1}",
                // #269/#272: a drill's hint and its audio slice live on the pinned version and the
                // stimulus - the two readers that reload a question and a summary.
                hint: i == 0 ? firstHint : null,
                passage: i == 0 ? firstPassage : null);
            examItemIds.Add(position.ExamItemId);
            itemIds.Add(position.ItemId);
            versionIds.Add(position.ItemVersionId);
        }

        await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, spkSec.SectionId, "speaking", "Speaking task", displayOrder: 1);

        var correctOpts = new List<int>();
        var wrongOpts = new List<int>();
        for (var i = 0; i < keys.Length; i++)
        {
            correctOpts.Add(await PoolFixture.OptionIdAsync(db, exam.ExamId, examItemIds[i], keys[i]));
            wrongOpts.Add(await PoolFixture.OptionIdAsync(db, exam.ExamId, examItemIds[i], keys[i] == "A" ? "B" : "A"));
        }

        return new PooledCertificate(
            cert.CertId, readSec.SectionId, spkSec.SectionId, exam.ExamId, itemIds, versionIds, examItemIds,
            correctOpts, wrongOpts);
    }

    private sealed record PooledFreeResponse(int CertId, int SectionId, int ExamId, int ItemId, int VersionId);

    /// <summary>
    /// The pool shape of <c>SeedFreeResponseCertificateAsync</c>: one section holding one productive
    /// task with a five-word floor and a word ceiling.
    /// </summary>
    private async Task<PooledFreeResponse> SeedPoolFreeResponseCertificateAsync(string certCode, string modality)
    {
        await EnsureTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var code = modality == "writing" ? "WRIT" : "SPEA";
        var skill = new Skill { CertId = cert.CertId, Code = code, Name = modality, Modality = modality, DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = modality, Code = "T1",
            Name = "Free response task", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 30,
            Purpose = modality == "writing" ? "writing_practice" : "speaking_practice",
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var position = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, modality, "Write about the meeting.",
            displayOrder: 1, minWords: 5, maxWords: 40, hint: "Noun form");

        return new PooledFreeResponse(cert.CertId, section.SectionId, exam.ExamId, position.ItemId, position.ItemVersionId);
    }

    // ---------------------------------------------------------------------------------------------
    // Legacy fixture. Kept for the ONE test below whose subject is the legacy store itself: the
    // evaluated word-floor/keyword feedback that only a Question row can produce. #269 deletes this
    // with the tables.
    // ---------------------------------------------------------------------------------------------

    private async Task<(int SectionId, int QuestionId)> SeedFreeResponseCertificateAsync(string certCode, string modality)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var code = modality == "writing" ? "WRIT" : "SPEA";
        var skill = new Skill { CertId = cert.CertId, Code = code, Name = modality, Modality = modality, DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = modality, Code = "T1",
            Name = "Free response task", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 30,
            Purpose = modality == "writing" ? "writing_practice" : "speaking_practice",
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var group = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = section.SectionId, DisplayOrder = 1 };
        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync();

        var question = new Question
        {
            GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId, SkillId = skill.SkillId,
            Stem = "Write about the meeting.", CefrLevel = CefrLevel.B1, QuestionType = "free_response",
            MinWords = 5, MaxWords = 40, RequiredKeywords = "proposal", Explanation = "Use the past simple.",
            DisplayOrder = 1,
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();

        return (section.SectionId, question.QuestionId);
    }

    [Fact]
    public async Task GetPracticeSections_returns_sections_with_available_question_counts()
    {
        var certCode = Unique("PR");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var view = await practice.GetPracticeSectionsAsync(userId);

        Assert.Equal(certCode, view.Certificate.Code);
        Assert.Single(view.ReadingSections);
        Assert.Equal(3, view.ReadingSections[0].AvailableQuestionCount);
        Assert.Equal("Incomplete Sentences", view.ReadingSections[0].Name);
        // #150: a speaking section has no typed drill, so it is listed apart from the drillable groups.
        Assert.Empty(view.OtherSections);
        Assert.Single(view.Speaking);
        Assert.Equal(0, view.Speaking[0].AvailableQuestionCount);
    }

    // #150: a certificate whose only skill is Writing had no route to a practice session at all -
    // CK_Practice_Modality allowed listening/reading only and PracticeService refused the rest.
    // Speaking is deliberately excluded (round 2): a spoken answer cannot be typed.
    //
    // #269: migrated as "the drill serves the task and records the answer", not "...and grades it".
    // The pool stores a productive answer UNGRADED (SubmitAnswerAsync's pinned branch fixes
    // IsCorrect = null and returns version.Explanation verbatim), so #150's sentence-level feedback
    // ("at least 5 words", "Missing required keywords") has no pool caller and is not asserted here.
    // What a writing drill must still do - start on a fresh certificate, serve a free-response task,
    // and keep the words the learner typed - is what remains, and it is unweakened.
    [Theory]
    [InlineData("writing")]
    public async Task StartSession_drills_a_free_response_section_and_records_the_answer(string modality)
    {
        var certCode = Unique("FR");
        var pooled = await SeedPoolFreeResponseCertificateAsync(certCode, modality);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        Assert.Equal(EnumCodes.Parse<Modality>(modality), session.Modality);

        var question = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.Equal(pooled.VersionId, question.ItemVersionId);
        Assert.Equal("free_response", question.QuestionType);

        var recorded = await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionId, new SubmitPracticeAnswerInput(ResponseText: "Too short."));
        Assert.Null(recorded.IsCorrect); // recorded, not judged
        Assert.Equal("Too short.", recorded.ResponseText);
        Assert.True(recorded.IsSessionFinished);
    }

    // #269: migrated as a RECORDED answer rather than a graded one. The brief's rule: a pool
    // productive answer is stored UNGRADED (IsCorrect null) forever, so the claim that survives is
    // "the response is recorded, and half of this test's own point is that no verdict is produced".
    // #150's evaluated feedback (word floor, required keywords) exists only on the legacy branch.
    [Theory]
    [InlineData("writing")]
    public async Task StartSession_records_a_free_response_answer_that_meets_the_question_requirements(string modality)
    {
        var certCode = Unique("FR");
        var pooled = await SeedPoolFreeResponseCertificateAsync(certCode, modality);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        var result = await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionId,
            new SubmitPracticeAnswerInput(ResponseText: "The team discussed the proposal yesterday."));

        Assert.Null(result.IsCorrect); // recorded, not judged - a pool productive answer is never graded
        Assert.Equal("The team discussed the proposal yesterday.", result.ResponseText);
    }

    // #150 round 2: a spoken answer cannot be typed. A speaking section is refused by the free drill
    // and pointed at the recorded, AI-graded speaking practice instead of being served a textarea.
    [Fact]
    public async Task StartSession_refuses_a_speaking_section_and_names_the_recorded_practice()
    {
        var certCode = Unique("SPK");
        var pooled = await SeedPoolFreeResponseCertificateAsync(certCode, "speaking");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId)));

        Assert.Equal(PracticeException.ModalityNotAllowed, ex.Code);
        Assert.Contains("record", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // #269: NOT migrated - the subject itself is the LEGACY store. The assertions are on #150's
    // EVALUATED feedback (the word floor and the missing keywords computed from the response), which
    // only PracticeService.Answers.cs:153-170's `isFreeResponse` branch produces from a Question row.
    // #272 re-checked it against HEAD and it still blocks: a pool productive answer is stored UNGRADED
    // (IsCorrect null) and its reply carries only the version's authored Explanation - measured on the
    // pool: `Explanation=Explanation 0`, no word-floor sentence. Substituting the authored copy would
    // assert a different, weaker fact, so this stays legacy with the tables.
    [Theory]
    [InlineData("writing")]
    public async Task Free_response_feedback_survives_a_reload_of_the_question_and_the_summary(string modality)
    {
        var certCode = Unique("FB");
        var (sectionId, questionId) = await SeedFreeResponseCertificateAsync(certCode, modality);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));
        await practice.SubmitAnswerAsync(
            userId, session.SessionId, questionId, new SubmitPracticeAnswerInput(ResponseText: "Too short."));

        var reread = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.Contains("at least 5 words", reread.Explanation);
        Assert.Contains("Missing required keywords", reread.Explanation);

        var summary = await practice.GetSummaryAsync(userId, session.SessionId);
        var outcome = Assert.Single(summary.Questions);
        Assert.Contains("at least 5 words", outcome.Explanation);
        Assert.Contains("Missing required keywords", outcome.Explanation);
    }

    /// <summary>
    /// D3: a session is finished by the SITTING, not by reading it.
    /// <para>
    /// Both the summary page and the AI-insight panel read an in-progress session and used to helpfully
    /// "finish it or ensure answers are final" on the way out. That is a state-machine transition taken
    /// by a GET, so a learner who opened the summary mid-drill - or whose summary page was prefetched -
    /// had the session closed underneath them, and the next submission was refused with
    /// <c>AlreadyAnswered</c> because the session was no longer in progress. Only a learner who
    /// genuinely answered the last question may finish a session; a read reports the state it found.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reading_a_summary_or_an_insight_does_not_finish_an_in_progress_session()
    {
        var certCode = Unique("RD");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        Assert.Equal(PracticeStatus.InProgress, session.Status);

        // A read. It must not move the session, and it must still answer something useful.
        var summary = await practice.GetSummaryAsync(userId, session.SessionId);
        Assert.Null(summary.FinishedAt);

        // The session is still sittable: the first question can be answered after the read. A pool
        // question is addressed by its version, a legacy one by its question id - the same rule
        // SubmitAnswerAsync already uses.
        var question = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        var graded = await practice.SubmitAnswerAsync(
            userId, session.SessionId, (question.QuestionId ?? question.ItemVersionId)!.Value,
            new SubmitPracticeAnswerInput(SelectedOptionId: pooled.CorrectOptionIds[0]));
        Assert.True(graded.IsCorrect);
    }

    // #150 round 2: a free-response answer has no options, so an "incorrect" row for it renders in
    // the saved bank as a question with no choices. Only objective answers feed that bank.
    [Theory]
    [InlineData("writing")]
    public async Task A_wrong_free_response_answer_is_not_filed_in_the_incorrect_answer_bank(string modality)
    {
        var certCode = Unique("SQ");
        var pooled = await SeedPoolFreeResponseCertificateAsync(certCode, modality);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionId, new SubmitPracticeAnswerInput(ResponseText: "Too short."));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.SavedQuestions.AnyAsync(sq => sq.UserId == userId && sq.ItemId == pooled.ItemId));
    }

    // Finding 4, the divergence itself: a writing section holding only keyed questions was counted by
    // the catalog (it has options) and then refused by the session builder (it serves free responses
    // only). One predicate now answers both questions, so the count is never a broken promise.
    //
    // #269/#272: NOT migrated - not a production gap, an unrepresentable shape. The pool makes the
    // shape this test exists for impossible: an ExamItem's modality is foreign-keyed to its section's,
    // and CK_Items_Modality_RenderFormat (docs/database-schema.sql:1104-1108) admits only
    // 'free_response' for a writing item, so a writing section can never hold a keyed position. The
    // two-filter divergence it caught cannot recur, so the test dies with the tables.
    [Fact]
    public async Task A_section_the_catalog_counts_can_always_be_started()
    {
        var certCode = Unique("DIV");
        var (writingSectionId, _) = await SeedFreeResponseCertificateAsync(certCode, "writing");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // The same writing section, now also holding one keyed question - the shape the two filters split on.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var group = new QuestionGroup
            {
                ExamId = await db.Questions.Where(q => q.SectionId == writingSectionId).Select(q => q.ExamId).FirstAsync(),
                CertId = await db.Sections.Where(s => s.SectionId == writingSectionId).Select(s => s.CertId).FirstAsync(),
                SectionId = writingSectionId,
                DisplayOrder = 2,
            };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            var keyed = new Question
            {
                GroupId = group.GroupId, ExamId = group.ExamId, SectionId = writingSectionId,
                SkillId = await db.Sections.Where(s => s.SectionId == writingSectionId).Select(s => s.SkillId).FirstAsync(),
                Stem = "Keyed question", CefrLevel = CefrLevel.B1, QuestionType = "mcq", DisplayOrder = 2,
            };
            db.Questions.Add(keyed);
            await db.SaveChangesAsync();

            db.QuestionOptions.Add(new QuestionOption
            {
                QuestionId = keyed.QuestionId, QuestionType = "mcq", Label = "A", Text = "Only option", IsCorrect = true,
            });
            await db.SaveChangesAsync();
        }

        var catalog = await practice.GetPracticeSectionsAsync(userId);
        var section = catalog.OtherSections.Single(s => s.SectionId == writingSectionId);
        Assert.Equal(2, section.AvailableQuestionCount);

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(writingSectionId));
        Assert.Equal(section.AvailableQuestionCount, session.QuestionCount);
    }

    // Finding 4: one predicate decides what a section can drill, shared by the catalog count and the
    // session builder. Two hand-written filters let a section advertise a drill it then refuses to
    // serve. This walks the real catalog and the real start call over the same sections.
    [Fact]
    public async Task Catalog_and_session_builder_agree_on_what_a_section_can_drill()
    {
        var certCode = Unique("AG");
        await SeedPoolMixedModalityCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var catalog = await practice.GetPracticeSectionsAsync(userId);
        var sections = catalog.ListeningSections.Concat(catalog.ReadingSections).Concat(catalog.OtherSections).ToList();
        Assert.Equal(3, sections.Count);

        foreach (var section in sections)
        {
            PracticeSessionInfo? session = null;
            PracticeException? refusal = null;
            try
            {
                session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(section.SectionId));
            }
            catch (PracticeException ex)
            {
                refusal = ex;
            }

            // The catalog count is the promise; the session delivers it or refuses for lack of questions.
            if (section.AvailableQuestionCount == 0)
            {
                Assert.Equal(PracticeException.NoQuestionsAvailable, refusal?.Code);
                continue;
            }

            Assert.Null(refusal);
            Assert.Equal(section.AvailableQuestionCount, session!.QuestionCount);
        }
    }

    /// <summary>
    /// A certificate with one section of each shape the builder has to tell apart: a reading section
    /// with stock, a reading section with none, and a writing section holding one productive task.
    /// <para>
    /// The old fixture's "writing section holding only keyed questions" is gone: the pool cannot
    /// express it (<c>CK_Items_Modality_RenderFormat</c> admits only <c>free_response</c> for a writing
    /// item), which is why the two-filter divergence it existed to catch is unreachable.
    /// </para>
    /// </summary>
    private async Task SeedPoolMixedModalityCertificateAsync(string certCode)
    {
        await EnsureTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var reading = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        var writing = new Skill { CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 2 };
        db.Skills.AddRange(reading, writing);
        await db.SaveChangesAsync();

        var keyedReading = new Section { CertId = cert.CertId, SkillId = reading.SkillId, Modality = "reading", Code = "P5", Name = "Keyed reading", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        var bareReading = new Section { CertId = cert.CertId, SkillId = reading.SkillId, Modality = "reading", Code = "P6", Name = "Empty reading", DisplayOrder = 2, OptionCount = 4, ExpectedQuestionCount = 1 };
        var writingSection = new Section { CertId = cert.CertId, SkillId = writing.SkillId, Modality = "writing", Code = "W1", Name = "Writing task", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
        db.Sections.AddRange(keyedReading, bareReading, writingSection);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 30, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, keyedReading.SectionId, "reading", "P5 question", displayOrder: 1);
        await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, writingSection.SectionId, "writing", "W1 question", displayOrder: 1,
            minWords: 5);
    }

    // The composite FK still owns the pin: a session's modality is its section's, and no widening of
    // the CHECK may create a way to make a listening session on a writing section.
    [Fact]
    public async Task A_practice_session_cannot_carry_a_modality_other_than_its_sections()
    {
        var certCode = Unique("PIN");
        var pooled = await SeedPoolFreeResponseCertificateAsync(certCode, "writing");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var user = new User
        {
            RoleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).FirstAsync(),
            Email = $"{Guid.NewGuid():N}@test.local",
            PasswordHash = new byte[] { 1 },
            FullName = "Pin Test",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.PracticeSessions.Add(new PracticeSession
        {
            UserId = user.UserId, CertId = pooled.CertId, SectionId = pooled.SectionId, Modality = "listening",
            QuestionCount = 1, Status = "in_progress", CorrectCount = 0,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StartSession_refuses_section_from_different_certificate()
    {
        var certCode1 = Unique("C1");
        var certCode2 = Unique("C2");
        var pooled1 = await SeedPoolCertificateAsync(certCode1);
        await SeedPoolCertificateAsync(certCode2);
        var userId = await SeedLearnerAsync(certCode2); // User is active in cert 2
        var (practice, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled1.SectionId)));

        Assert.Equal(PracticeException.SectionUnavailable, ex.Code);
    }

    [Fact]
    public async Task StartSession_creates_session_and_answers_for_mcq_questions_only()
    {
        var certCode = Unique("MC");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // #155: the session is the whole bank when the bank is smaller than ten - three here - rather
        // than the learner's chosen two.
        Assert.Equal(PracticeStatus.InProgress, session.Status);
        Assert.Equal(3, session.QuestionCount);
        Assert.Equal(0, session.CorrectCount);

        var q1 = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.Equal(pooled.VersionIds[0], q1.ItemVersionId);
        Assert.Equal(1, q1.DisplayOrder);
        Assert.Equal(3, q1.TotalQuestions);
        Assert.False(q1.HintUsed);
        Assert.Null(q1.Hint); // Not revealed yet
        Assert.Null(q1.IsCorrect); // Not answered yet
        Assert.Null(q1.CorrectOptionId); // Cannot leak answer
    }

    // #269: migrated. #272 re-checked it against HEAD: RevealHintAsync resolves the row by ItemVersionId
    // and returns the pinned version's Hint, and the reload reads `pinned?.Hint ?? q?.Hint`
    // (PracticeService.Questions.cs:220-222) - the pair db7e969 fixed. The drill records the hint AND
    // displays it on the pool now, so the legacy fixture is no longer needed for this.
    [Fact]
    public async Task RevealHint_records_hint_used_on_practice_answer()
    {
        var certCode = Unique("HN");
        var pooled = await SeedPoolCertificateAsync(certCode, firstHint: "Hint 1");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        var hint = await practice.RevealHintAsync(userId, session.SessionId, pooled.VersionIds[0]);

        Assert.Equal("Hint 1", hint);

        var q1 = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.True(q1.HintUsed);
        Assert.Equal("Hint 1", q1.Hint);
    }

    [Fact]
    public async Task SubmitAnswer_grades_answer_immediately_and_shows_explanation()
    {
        var certCode = Unique("GR");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Submit correct answer to the first position.
        var result1 = await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        Assert.True(result1.IsCorrect);
        Assert.Equal(pooled.CorrectOptionIds[0], result1.SelectedOptionId);
        Assert.Equal("Explanation 1", result1.Explanation);
        Assert.False(result1.IsSessionFinished);
        Assert.Equal(2, result1.NextQuestionDisplayOrder);

        // Submit wrong answer to Q2. The session is three questions (the whole bank), so answering two
        // of them does not finish it - #155 made the count the planner's, not the learner's.
        var result2 = await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionIds[1], new SubmitPracticeAnswerInput(pooled.WrongOptionIds[1]));

        Assert.False(result2.IsCorrect);
        Assert.Equal(pooled.WrongOptionIds[1], result2.SelectedOptionId);
        Assert.Equal(pooled.CorrectOptionIds[1], result2.CorrectOptionId);
        Assert.Equal("Explanation 2", result2.Explanation);
        Assert.False(result2.IsSessionFinished);
        Assert.Equal(3, result2.NextQuestionDisplayOrder);

        // Q3 finishes it.
        var result3 = await practice.SubmitAnswerAsync(
            userId, session.SessionId, pooled.VersionIds[2], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[2]));
        Assert.True(result3.IsSessionFinished);

        var info = await practice.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.Finished, info.Status);
        Assert.Equal(2, info.CorrectCount);
    }

    [Fact]
    public async Task SubmitAnswer_rejects_option_from_different_question()
    {
        var certCode = Unique("OP");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Try submitting the second position's option for the first
        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[1])));

        Assert.Equal(PracticeException.InvalidOption, ex.Code);
    }

    // #269: migrated. #272 re-checked the two blockers this comment named, and both are gone:
    // PracticeService.Results.cs:86-89 now reads AudioStartMs/AudioEndMs off the stimulus (db7e969), and
    // the reloaded question renders the pinned version's hint (PracticeService.Questions.cs:220-222). The
    // audio slice moves from the legacy QuestionGroup to the position's stimulus, which is where a pool
    // paper keeps it - the only edit to the fixture after seeding.
    [Fact]
    public async Task GetSummary_computes_per_question_outcome_and_skip_accuracy_math()
    {
        var certCode = Unique("SM");
        var pooled = await SeedPoolCertificateAsync(certCode, firstHint: "Hint 1", firstPassage: "A short practice transcript.");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Q1: reveal hint and answer correctly
        await practice.RevealHintAsync(userId, session.SessionId, pooled.VersionIds[0]);
        await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        // Q2: answer incorrectly without hint
        await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[1], new SubmitPracticeAnswerInput(pooled.WrongOptionIds[1]));

        // Q3: skip (SelectedOptionId = null)
        await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[2], new SubmitPracticeAnswerInput(null));

        await database.SqlAsync($"""
            UPDATE s
            SET s.AudioPath = N'/audio/practice-dialogue.mp3', s.AudioStartMs = 200, s.AudioEndMs = 800,
                s.Transcript = N'A short practice transcript.'
            FROM Stimuli s
            JOIN Items i ON i.StimulusId = s.StimulusId
            JOIN ExamItems ei ON ei.ItemId = i.ItemId
            WHERE ei.ExamItemId = {pooled.ExamItemIds[0]};
            """);

        var summary = await practice.GetSummaryAsync(userId, session.SessionId);

        Assert.Equal(3, summary.TotalQuestions);
        Assert.Equal(2, summary.AnsweredCount); // Only Q1 & Q2 answered
        Assert.Equal(1, summary.CorrectCount);
        Assert.Equal(1, summary.IncorrectCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(50, summary.AccuracyPercentage); // 1 / 2 = 50%
        Assert.Equal(1, summary.HintUsedCount);
        Assert.Equal(1, summary.CorrectWithHintCount);
        Assert.Equal(0, summary.IncorrectWithHintCount);

        Assert.Equal(3, summary.Questions.Count);
        Assert.True(summary.Questions[0].IsCorrect);
        Assert.True(summary.Questions[0].HintUsed);
        Assert.Equal("/audio/practice-dialogue.mp3", summary.Questions[0].AudioPath);
        Assert.Equal(200, summary.Questions[0].AudioStartMs);
        Assert.Equal(800, summary.Questions[0].AudioEndMs);
        Assert.Equal("A short practice transcript.", summary.Questions[0].Transcript);
        Assert.False(summary.Questions[1].IsCorrect);
        Assert.False(summary.Questions[1].HintUsed);
        Assert.Null(summary.Questions[2].IsCorrect); // Skipped
    }

    // #155: the all/wrong_before/unseen selector is gone. What replaced it is mastery state, so the
    // same scenario now reads: a wrong answer holds its question back and the next session is made of
    // what is still due.
    [Fact]
    public async Task A_session_is_drawn_from_what_is_due_not_from_a_learner_picked_source()
    {
        var certCode = Unique("QS");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        Assert.Equal(3, session1.QuestionCount); // the whole bank: one short session, honestly short
        await practice.SubmitAnswerAsync(
            userId, session1.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.WrongOptionIds[0]));

        // The next session is not "wrong_before" - it is everything still due, which excludes exactly
        // the question just missed and its whole category.
        var session2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        var session2Questions = await practice.GetSessionQuestionsAsync(userId, session2.SessionId);

        Assert.DoesNotContain(pooled.VersionIds[0], session2Questions.Select(q => q.ItemVersionId));
        Assert.Equal(2, session2.QuestionCount);
    }

    [Fact]
    public async Task StartSession_prunes_older_sessions_and_answer_logs_when_exceeding_limit()
    {
        var certCode = Unique("PR");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();
        practice.MaxSavedSessionsPerCertificate = 2; // Test limit of 2

        // Session 1: complete it
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        await practice.SubmitAnswerAsync(userId, s1.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        // Session 2: complete it
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        await practice.SubmitAnswerAsync(userId, s2.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        // Session 3: start it -> exceeds limit of 2, so s1 and its answers should be pruned
        var s3 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var remainingSessions = await db.PracticeSessions
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SessionId)
            .Select(s => s.SessionId)
            .ToListAsync();

        Assert.Equal(2, remainingSessions.Count);
        Assert.DoesNotContain(s1.SessionId, remainingSessions);
        Assert.Contains(s2.SessionId, remainingSessions);
        Assert.Contains(s3.SessionId, remainingSessions);

        // s1's answers should also have been deleted
        var s1Answers = await db.PracticeAnswers.Where(pa => pa.SessionId == s1.SessionId).ToListAsync();
        Assert.Empty(s1Answers);

        // s2 and s3 answers exist
        var s2Answers = await db.PracticeAnswers.Where(pa => pa.SessionId == s2.SessionId).ToListAsync();
        Assert.NotEmpty(s2Answers);
    }

    [Fact]
    public async Task StartSession_cleans_up_empty_unstarted_in_progress_sessions()
    {
        var certCode = Unique("AB");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // Start session 1 but do not answer any questions
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Start session 2 -> s1 was empty, so it should be cleaned up immediately
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var sessions = await db.PracticeSessions.Where(s => s.UserId == userId).ToListAsync();

        Assert.Single(sessions);
        Assert.Equal(s2.SessionId, sessions[0].SessionId);

        var s1Answers = await db.PracticeAnswers.Where(pa => pa.SessionId == s1.SessionId).ToListAsync();
        Assert.Empty(s1Answers);
    }

    [Fact]
    public async Task StartSession_marks_partially_answered_in_progress_session_as_abandoned()
    {
        var certCode = Unique("PA");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // Start session 1 and answer question 1
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));
        await practice.SubmitAnswerAsync(userId, s1.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        // Start session 2 -> s1 had an answer, so it should be preserved as "abandoned"
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var session1InDb = await db.PracticeSessions.SingleOrDefaultAsync(s => s.SessionId == s1.SessionId);

        Assert.NotNull(session1InDb);
        Assert.Equal("abandoned", session1InDb.Status);
        Assert.NotNull(session1InDb.FinishedAt);
    }

    [Fact]
    public async Task GetPracticeSections_returns_unfinished_session_when_in_progress()
    {
        var certCode = Unique("UP");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        var catalog = await practice.GetPracticeSectionsAsync(userId);

        Assert.NotNull(catalog.UnfinishedSession);
        Assert.Equal(s1.SessionId, catalog.UnfinishedSession.SessionId);
        Assert.Equal(PracticeStatus.InProgress, catalog.UnfinishedSession.Status);
        Assert.Equal(pooled.SectionId, catalog.UnfinishedSession.SectionId);
    }

    [Fact]
    public async Task GetSessionQuestions_returns_all_questions_with_anti_cheat_rules_and_saved_state()
    {
        var certCode = Unique("SQ");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Answer question 1
        await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));

        // Question 2: save to bank
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.SavedQuestions.Add(new SavedQuestion
            {
                UserId = userId,
                CertId = pooled.CertId,
                ItemId = pooled.ItemIds[1],
                ItemVersionId = pooled.VersionIds[1],
                Source = "manual",
                SavedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Question 3 remains unanswered

        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);

        Assert.Equal(3, questions.Count);
        Assert.Equal(pooled.VersionIds[0], questions[0].ItemVersionId);
        Assert.Equal(pooled.VersionIds[1], questions[1].ItemVersionId);
        Assert.Equal(pooled.VersionIds[2], questions[2].ItemVersionId);

        // Anti-cheat policy verification:
        // Question 1 was answered -> correctOptionId & explanation released
        Assert.True(questions[0].IsCorrect);
        Assert.NotNull(questions[0].CorrectOptionId);
        Assert.NotNull(questions[0].Explanation);

        // Question 2 was not answered, but saved -> correctOptionId & explanation withheld, IsSaved = true
        Assert.Null(questions[1].CorrectOptionId);
        Assert.Null(questions[1].Explanation);
        Assert.True(questions[1].IsSaved);

        // Question 3 was not answered -> correctOptionId & explanation withheld, IsSaved = false
        Assert.Null(questions[2].CorrectOptionId);
        Assert.Null(questions[2].Explanation);
        Assert.False(questions[2].IsSaved);
    }

    [Fact]
    public async Task SubmitAnswer_out_of_order_does_not_finish_session_when_questions_remain()
    {
        var certCode = Unique("NO");
        var pooled = await SeedPoolCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(pooled.SectionId));

        // Jump directly to Question 3 (the last question) and answer it
        var resultQ3 = await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[2], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[2]));

        // Session must NOT be finished yet because Q1 and Q2 remain unanswered
        Assert.False(resultQ3.IsSessionFinished);

        var sessionInfo = await practice.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.InProgress, sessionInfo.Status);

        // Now answering Question 1 and Question 2 should succeed without SessionAlreadyFinished
        var resultQ1 = await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[0], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[0]));
        Assert.False(resultQ1.IsSessionFinished);

        var resultQ2 = await practice.SubmitAnswerAsync(userId, session.SessionId, pooled.VersionIds[1], new SubmitPracticeAnswerInput(pooled.CorrectOptionIds[1]));
        // Now all questions are answered -> session finishes!
        Assert.True(resultQ2.IsSessionFinished);

        var finalSessionInfo = await practice.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.Finished, finalSessionInfo.Status);
    }

    // #269: NOT migrated - a production gap, re-measured against HEAD 37e0d48 in #272 and still present.
    // SubmitAnswerAsync's pinned branch (PracticeService.Answers.cs:125-139) reads only
    // `input.SelectedOptionId`: a SelectedOptionIds LIST is written to Json but leaves IsCorrect null,
    // and GradedPracticeAnswerResult.CorrectOptionIds (PracticeService.Answers.cs:349-351) emits the
    // single first key label rather than the set. Measured on a pool multi_select position keyed 'A,B':
    // IsCorrect=null, CorrectOptionIds=[31] where the two answered ids were [31,32]. A pool multi_select
    // therefore records no verdict at all, which is the defect, not a test limitation.
    [Fact]
    public async Task SubmitAnswer_evaluates_multi_select_accurately()
    {
        var certCode = Unique("MS");
        int secId;
        int q1Id, q2Id;
        int opt1AId, opt1BId, opt1CId;
        int opt2AId, opt2BId;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var sec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P7", Name = "Multi Select Section", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 1 };
            db.Sections.Add(sec);
            await db.SaveChangesAsync();
            secId = sec.SectionId;

            var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId, DisplayOrder = 1 };
            db.QuestionGroups.Add(grp);
            await db.SaveChangesAsync();

            var q1 = new Question
            {
                GroupId = grp.GroupId,
                ExamId = exam.ExamId,
                SectionId = sec.SectionId,
                SkillId = skill.SkillId,
                Stem = "Select all correct options - Q1",
                CefrLevel = CefrLevel.B2,
                QuestionType = "multi_select",
                MaxSelections = 2,
                Hint = "Check both options",
                Explanation = "Options A and B are correct for Q1",
                DisplayOrder = 1
            };
            var q2 = new Question
            {
                GroupId = grp.GroupId,
                ExamId = exam.ExamId,
                SectionId = sec.SectionId,
                SkillId = skill.SkillId,
                Stem = "Select all correct options - Q2",
                CefrLevel = CefrLevel.B2,
                QuestionType = "multi_select",
                MaxSelections = 2,
                Hint = "Check both options",
                Explanation = "Options A and B are correct for Q2",
                DisplayOrder = 2
            };
            db.Questions.AddRange(q1, q2);
            await db.SaveChangesAsync();
            q1Id = q1.QuestionId;
            q2Id = q2.QuestionId;

            var o1A = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "multi_select", Label = "A", Text = "Opt 1A", IsCorrect = true };
            var o1B = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "multi_select", Label = "B", Text = "Opt 1B", IsCorrect = true };
            var o1C = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "multi_select", Label = "C", Text = "Opt 1C", IsCorrect = false };

            var o2A = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "multi_select", Label = "A", Text = "Opt 2A", IsCorrect = true };
            var o2B = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "multi_select", Label = "B", Text = "Opt 2B", IsCorrect = true };

            db.QuestionOptions.AddRange(o1A, o1B, o1C, o2A, o2B);
            await db.SaveChangesAsync();

            opt1AId = o1A.OptionId;
            opt1BId = o1B.OptionId;
            opt1CId = o1C.OptionId;
            opt2AId = o2A.OptionId;
            opt2BId = o2B.OptionId;
        }

        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId));

        // Question 1: Submit incomplete (only opt1AId) -> IsCorrect = false
        var partialResult = await practice.SubmitAnswerAsync(userId, session.SessionId, q1Id,
            new SubmitPracticeAnswerInput(SelectedOptionIds: new List<int> { opt1AId }));
        Assert.False(partialResult.IsCorrect);
        Assert.NotNull(partialResult.CorrectOptionIds);
        Assert.Contains(opt1AId, partialResult.CorrectOptionIds);
        Assert.Contains(opt1BId, partialResult.CorrectOptionIds);
        Assert.Equal("Options A and B are correct for Q1", partialResult.Explanation);

        // Question 2: Submit complete (opt2AId and opt2BId) -> IsCorrect = true
        var correctResult = await practice.SubmitAnswerAsync(userId, session.SessionId, q2Id,
            new SubmitPracticeAnswerInput(SelectedOptionIds: new List<int> { opt2AId, opt2BId }));
        Assert.True(correctResult.IsCorrect);
        Assert.NotNull(correctResult.CorrectOptionIds);
        Assert.Contains(opt2AId, correctResult.CorrectOptionIds);
        Assert.Contains(opt2BId, correctResult.CorrectOptionIds);
        Assert.True(correctResult.IsSessionFinished);
    }

    // #269: NOT migrated - a production gap, re-measured against HEAD 37e0d48 in #272 and still present.
    // SubmitAnswerAsync's pinned branch (PracticeService.Answers.cs:125-139) grades only from
    // `input.SelectedOptionId`, so a text answer stores IsCorrect = null even though the version carries
    // AcceptedAnswersJson. The two stores now disagree in a way they did not before 9097ba7: the ENGINE
    // grades it (AttemptEngine.Answers.cs:105-107 -> EvaluatePinnedText) and PracticeService does not.
    // The reply's AcceptedAnswersJson comes from `q?.AcceptedAnswersJson` (PracticeService.Answers.cs:348)
    // and the projection nulls it (PracticeService.Questions.cs:172). Measured on a pool inline_completion
    // version whose AcceptedAnswersJson is ["development","software development"], answered
    // "  DEVELOPMENT  ": IsCorrect=null, AcceptedAnswersJson=null.
    [Fact]
    public async Task SubmitAnswer_evaluates_inline_completion_accurately()
    {
        var certCode = Unique("IC");
        int secId;
        int q1Id, q2Id;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var sec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Completion Section", DisplayOrder = 1, ExpectedQuestionCount = 1 };
            db.Sections.Add(sec);
            await db.SaveChangesAsync();
            secId = sec.SectionId;

            var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId, DisplayOrder = 1 };
            db.QuestionGroups.Add(grp);
            await db.SaveChangesAsync();

            var q1 = new Question
            {
                GroupId = grp.GroupId,
                ExamId = exam.ExamId,
                SectionId = sec.SectionId,
                SkillId = skill.SkillId,
                Stem = "Fill in the blank for Q1",
                CefrLevel = CefrLevel.B2,
                QuestionType = "inline_completion",
                AcceptedAnswersJson = "[\"development\", \"software development\"]",
                AllowNumber = false,
                MaxWords = 2,
                Hint = "Noun form",
                Explanation = "The correct answer is development",
                DisplayOrder = 1
            };
            var q2 = new Question
            {
                GroupId = grp.GroupId,
                ExamId = exam.ExamId,
                SectionId = sec.SectionId,
                SkillId = skill.SkillId,
                Stem = "Fill in the blank for Q2",
                CefrLevel = CefrLevel.B2,
                QuestionType = "inline_completion",
                AcceptedAnswersJson = "[\"apple\", \"apples\"]",
                AllowNumber = false,
                MaxWords = 1,
                Hint = "A fruit",
                Explanation = "The fruit is apple",
                DisplayOrder = 2
            };
            db.Questions.AddRange(q1, q2);
            await db.SaveChangesAsync();
            q1Id = q1.QuestionId;
            q2Id = q2.QuestionId;
        }

        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId));

        // Q1: Whitespace & case insensitive match -> IsCorrect = true
        var res1 = await practice.SubmitAnswerAsync(userId, session.SessionId, q1Id,
            new SubmitPracticeAnswerInput(ResponseText: "  DEVELOPMENT  "));
        Assert.True(res1.IsCorrect);
        Assert.Equal("[\"development\", \"software development\"]", res1.AcceptedAnswersJson);
        Assert.Equal("The correct answer is development", res1.Explanation);

        // Q2: Incorrect word -> IsCorrect = false
        var res2 = await practice.SubmitAnswerAsync(userId, session.SessionId, q2Id,
            new SubmitPracticeAnswerInput(ResponseText: "banana"));
        Assert.False(res2.IsCorrect);
        Assert.Equal("[\"apple\", \"apples\"]", res2.AcceptedAnswersJson);
        Assert.True(res2.IsSessionFinished);
    }

    // #269: NOT migrated - a production gap, re-measured against HEAD 37e0d48 in #272 and still present.
    // The pinned branch nulls the answer-shape fields deliberately: maxSelections, acceptedAnswersJson,
    // requiredKeywords, minWords and maxWords at PracticeService.Questions.cs:170-175, and the type is
    // read from the item's shape at :169. Measured on a pool inline_completion position:
    // QuestionType=free_response (the version has no options, so an authored blank renders as a
    // productive task), AcceptedAnswersJson=null, MaxWords=null, AllowNumber=True. Three of the five
    // assertions below therefore have no pool answer, and the rendered type is not the authored one.
    [Fact]
    public async Task GetSessionTakeAsync_returns_component_metadata()
    {
        var certCode = Unique("TK");
        int secId;
        int qId;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var sec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Take Section", DisplayOrder = 1, ExpectedQuestionCount = 1 };
            db.Sections.Add(sec);
            await db.SaveChangesAsync();
            secId = sec.SectionId;

            var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId, DisplayOrder = 1 };
            db.QuestionGroups.Add(grp);
            await db.SaveChangesAsync();

            var q = new Question
            {
                GroupId = grp.GroupId,
                ExamId = exam.ExamId,
                SectionId = sec.SectionId,
                SkillId = skill.SkillId,
                Stem = "Take Question Stem",
                CefrLevel = CefrLevel.B2,
                QuestionType = "inline_completion",
                AcceptedAnswersJson = "[\"solution\"]",
                AllowNumber = false,
                MaxWords = 1,
                Hint = "Hint text",
                Explanation = "Detailed solution",
                DisplayOrder = 1
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            qId = q.QuestionId;
        }

        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId));
        var take = await practice.GetSessionTakeAsync(userId, session.SessionId);

        Assert.NotNull(take);
        Assert.Equal(session.SessionId, take.Session.SessionId);
        Assert.Single(take.Questions);
        Assert.Single(take.Navigator);

        var currentQ = take.Question;
        Assert.Equal(qId, currentQ.QuestionId);
        Assert.Equal("inline_completion", currentQ.QuestionType);
        Assert.Equal("[\"solution\"]", currentQ.AcceptedAnswersJson);
        Assert.False(currentQ.AllowNumber);
        Assert.Equal(1, currentQ.MaxWords);
        // Anti-cheat: not answered yet -> explanation hidden
        Assert.Null(currentQ.Explanation);

        // Submit answer
        await practice.SubmitAnswerAsync(userId, session.SessionId, qId, new SubmitPracticeAnswerInput(ResponseText: "solution"));

        // Refetch take
        var takeAfter = await practice.GetSessionTakeAsync(userId, session.SessionId);
        Assert.True(takeAfter.Question.IsCorrect);
        Assert.Equal("Detailed solution", takeAfter.Question.Explanation);
    }
}
