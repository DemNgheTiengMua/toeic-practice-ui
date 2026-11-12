using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #155: practice becomes real revision. One test per rule of the four-state mastery machine, one per
/// cooldown rule, and one for the 10-question Smart Practice mix - all against real SQL Server, real
/// triggers and the schema loaded from docs/database-schema.sql.
/// <para>
/// <b>#269: the unit of mastery is the cell, not the category.</b> A pool drill writes
/// <c>UserCellMasteries</c> keyed by the item's own <c>(TopicId, CefrLevel)</c>, and it writes no
/// per-question history at all - the cooldown is derived from the practice answers themselves. So the
/// fixtures here author pool stock and the assertions read the cell row
/// (<see cref="MasteryService"/>) rather than <c>UserCategoryMasteries</c>.
/// </para>
/// <para>
/// The numbers are the cell machine's, which are not the category machine's: <c>n_min</c> is 12
/// objective observations rather than 10, and there is no streak or session-day counter. Where an old
/// assertion carried a number that changed, the new one is derived from
/// <see cref="MasteryCellMachine"/> rather than adjusted by hand.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PracticeMasteryTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (PracticeService Practice, ManualTimeProvider Clock) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        // #269: the drill stamps answered rows with DateTime.UtcNow while every cooldown read compares
        // against this clock, so the two must start from the same instant or "24 hours later" would be
        // measured against a date nobody stored. The assertions stay relative, which is what they mean.
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        return (new PracticeService(db, learnerService, clock: clock), clock);
    }

    private async Task<int> SeedLearnerAsync(string activeCertCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{activeCertCode}'));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// The canonical twenty topic vocabulary. #240: the trigger refuses a twenty-first, so a fixture
    /// seeds the set rather than inventing a topic.
    /// </summary>
    private Task SeedTopicsAsync() => database.SqlAsync("""
        INSERT INTO Topics (Modality, Code, Name)
        SELECT v.Modality, v.Code, v.Name
        FROM (VALUES
          ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
          ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
          ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
          ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
          ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
          ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
          ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
          ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
          ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
          ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
        ) v(Modality, Code, Name)
        WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);
        """);

    /// <summary>
    /// A certificate with one reading section holding <paramref name="questionCount"/> keyed pool
    /// positions, all in one cell (one topic, one level), each with key A.
    /// <para>
    /// One cell is what makes the mastery rules observable: every position a session serves moves the
    /// same <c>UserCellMasteries</c> row, so "five correct answers" is five observations of that row.
    /// </para>
    /// <para>
    /// The cell is shared with every other reading fixture in the suite (the topic vocabulary is a
    /// closed twenty), but the section drill never sees them: its candidates are the positions bound to
    /// <em>this</em> section of <em>this</em> certificate, so a neighbour's stock cannot outrank it.
    /// </para>
    /// </summary>
    private async Task<SeededCell> SeedCellAsync(string certCode, int questionCount = 12)
    {
        await SeedTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var sec = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5",
            Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = questionCount,
        };
        db.Sections.Add(sec);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var versions = new List<int>();
        for (var i = 0; i < questionCount; i++)
        {
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, sec.SectionId, "reading", $"Stem {i + 1}",
                displayOrder: i + 1, keyLabel: "A", level: "B2", explanation: $"Explanation {i + 1}");
            versions.Add(position.ItemVersionId);
        }

        var topicId = await db.Items
            .Where(i => i.ItemVersions.Any(v => v.ItemVersionId == versions[0]))
            .Select(i => i.TopicId)
            .SingleAsync();

        return new SeededCell(cert.CertId, sec.SectionId, topicId, CefrLevel.B2, versions);
    }

    private sealed record SeededCell(
        int CertId, int SectionId, int TopicId, CefrLevel Level, List<int> VersionIds);

    /// <summary>
    /// The cell's stored mastery row, read through the one reader. #237: this is the row a pool answer
    /// moves, and it is keyed by (topic, level) rather than by a category.
    /// <para>
    /// <c>CellMasteryView</c> is a struct, so its nullable form is not an "object" a test can
    /// <c>NotNull</c> and then dereference. A null row means the assertion about to be made is
    /// meaningless, so it throws here instead of returning a nullable.
    /// </para>
    /// </summary>
    private async Task<CellMasteryView> ReadCellAsync(int userId, int topicId, CefrLevel level)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await new MasteryService(db).ReadAsync(userId, topicId, level)
            ?? throw new InvalidOperationException($"Cell ({topicId}, {level}) has no mastery row.");
    }

    /// <summary>The same read, for the one test that asserts the row does not exist yet.</summary>
    private async Task<CellMasteryView?> ReadCellOrNullAsync(int userId, int topicId, CefrLevel level)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await new MasteryService(db).ReadAsync(userId, topicId, level);
    }

    /// <summary>
    /// The answer that takes the pool item's key, and one that does not. #269: the option ids are the
    /// projection's, read from the question the drill served, never recomputed from a formula.
    /// </summary>
    private static SubmitPracticeAnswerInput Right(PracticeQuestionView q) =>
        new(q.Options.Single(o => o.Label == "A").OptionId);

    private static SubmitPracticeAnswerInput Wrong(PracticeQuestionView q) =>
        new(q.Options.Single(o => o.Label == "B").OptionId);

    /// <summary>Starts one session and answers every question in it correctly. A session is where a
    /// learner actually meets a cell, and the cell is then rested until its next due date - so a test
    /// that wants more answers moves the clock forward, not starts another session.</summary>
    private static async Task<int> AnswerSessionCorrectlyAsync(PracticeService practice, int userId, int sectionId)
    {
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        foreach (var question in questions)
        {
            await practice.SubmitAnswerAsync(userId, session.SessionId, question.ItemVersionId!.Value, Right(question));
        }

        return questions.Count;
    }

    // ---- Rule 1: a wrong answer moves the cell to Learning and sets a 24-hour review ----

    [Fact]
    public async Task A_wrong_answer_moves_the_category_to_learning_and_sets_a_24_hour_review()
    {
        var certCode = Unique("ML");
        var seeded = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Untested is "no row yet": nothing is written before the first answer.
        Assert.Null(await ReadCellOrNullAsync(userId, seeded.TopicId, seeded.Level));

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].ItemVersionId!.Value, Wrong(questions[0]));

        var mastery = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.NotNull(mastery);
        Assert.Equal(MasteryState.Learning, mastery.State);
        Assert.Equal(1, mastery.ObservationCount);
        Assert.Equal(1, mastery.ConsecutiveWrong);
        Assert.Equal(clock.GetUtcNow().AddHours(24), mastery.NextReviewDueAt);
    }

    // ---- Rule 2: the item just answered wrongly is not served again during its cooldown ----

    [Fact]
    public async Task A_question_answered_wrongly_is_not_offered_again_before_its_cooldown_expires()
    {
        var certCode = Unique("QC");
        // Ten items and a ten-item session, so the assertion below is about the cooldown and not about
        // a short bank: before the cooldown expires nine items are servable and the missed one is not.
        var seeded = await SeedCellAsync(certCode, questionCount: 10);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var first = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, first.SessionId);
        var missed = questions[0];
        await practice.SubmitAnswerAsync(userId, first.SessionId, missed.ItemVersionId!.Value, Wrong(missed));

        // Started straight away, so nothing has had time to become due again.
        var second = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var secondQuestions = await practice.GetSessionQuestionsAsync(userId, second.SessionId);
        Assert.DoesNotContain(missed.ItemVersionId, secondQuestions.Select(q => q.ItemVersionId));
        Assert.Equal(9, second.QuestionCount);

        // Read the stored answer too: a short session could pass the assertion above for the wrong
        // reason. #269: the hold is the answer row itself - no per-question history exists.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.PracticeAnswers.AsNoTracking()
            .SingleAsync(a => a.SessionId == first.SessionId && a.ItemVersionId == missed.ItemVersionId);
        Assert.False(stored.IsCorrect);
        Assert.Equal(seeded.CertId, await CertIdOfAsync(seeded.SectionId));
    }

    private async Task<int> CertIdOfAsync(int sectionId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Sections.Where(s => s.SectionId == sectionId).Select(s => s.CertId).SingleAsync();
    }

    // ---- Rule 3: 24 hours later the same item comes back ----

    [Fact]
    public async Task A_question_returns_after_its_24_hour_cooldown_expires()
    {
        var certCode = Unique("QR");
        // Ten items: a ten-item session therefore has to include the eighth-tenth seen items once
        // their cooldown has lapsed, which is what this test is about.
        var seeded = await SeedCellAsync(certCode, questionCount: 10);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var first = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, first.SessionId);
        var missed = questions[0];
        await practice.SubmitAnswerAsync(userId, first.SessionId, missed.ItemVersionId!.Value, Wrong(missed));

        var during = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.DoesNotContain(missed.ItemVersionId,
            (await practice.GetSessionQuestionsAsync(userId, during.SessionId)).Select(q => q.ItemVersionId));

        clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));

        // #269: the hold is derived from the answer row, so the lapsed item is servable again and the
        // ten-item draw has room for it.
        var after = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Contains(missed.ItemVersionId,
            (await practice.GetSessionQuestionsAsync(userId, after.SessionId)).Select(q => q.ItemVersionId));
    }

    // ---- Rule 4: a cell in cooldown is not chosen while a due cell exists ----
    //
    // #269: NOT migrated. The catalog reports a section's rest from UserCategoryMasteries
    // (PracticeService.Catalog.cs LoadMasteryAsync -> PracticeService.Mastery.cs reads
    // UserCategoryMasteries), while a pool answer writes UserCellMasteries. A stocked pool section
    // therefore always reports CategoriesInCooldown = 0 and NextReviewDueAt = null, so there is no
    // pool fact for "the catalog says when this returns" to assert. The drill does exclude the missed
    // item (Rule 2 and Rule 3 above), so only the catalog's reporting half is missing.
    [Fact]
    public async Task A_category_in_cooldown_is_passed_over_for_a_due_category_and_says_when_it_returns()
    {
        var certCode = Unique("CC");
        var seeded = await SeedCellAsync(certCode, questionCount: 12);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Put the cell into cooldown and leave the section's other cells untouched.
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        var missed = questions[0];
        await practice.SubmitAnswerAsync(userId, session.SessionId, missed.ItemVersionId!.Value, Wrong(missed));

        // The cell's own row carries the review date the catalog ought to report.
        var cell = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(clock.GetUtcNow().AddHours(24), cell.NextReviewDueAt);

        var next = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.DoesNotContain(missed.ItemVersionId,
            (await practice.GetSessionQuestionsAsync(userId, next.SessionId)).Select(q => q.ItemVersionId));

        clock.Advance(TimeSpan.FromHours(25));

        // The cooldown has lapsed, so the cell the catalog should stop reporting as resting is due,
        // and the drill serves its positions again.
        var after = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.True(after.IsDue(clock.GetUtcNow()));

        var due = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Contains(missed.ItemVersionId,
            (await practice.GetSessionQuestionsAsync(userId, due.SessionId)).Select(q => q.ItemVersionId));
    }

    // ---- Rule 5: correct answers take Learning to Reviewing, spaced +3 days ----

    [Fact]
    public async Task Five_correct_answers_in_a_row_move_the_category_from_learning_to_reviewing()
    {
        var certCode = Unique("LC");
        // Six positions: one is missed (and held out of the retry by its cooldown), leaving five
        // servable for the corrective session. One wrong + five correct is six observations, which is
        // the cell machine's Reviewing threshold - a run that reached Mastered would hide this rule.
        var seeded = await SeedCellAsync(certCode, questionCount: 6);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].ItemVersionId!.Value, Wrong(questions[0]));
        Assert.Equal(MasteryState.Learning, (await ReadCellAsync(userId, seeded.TopicId, seeded.Level)).State);

        // #269: the item cooldown is the answer row's own 24 hours, so the clock moves to just inside
        // it. The missed position is still held out, exactly five servable positions remain, and the
        // cell therefore reaches six observations - the Reviewing threshold, not Mastered's twelve.
        clock.Advance(TimeSpan.FromHours(23));
        var answered = await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);
        Assert.Equal(5, answered);

        var mastery = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.NotNull(mastery);
        Assert.Equal(MasteryState.Reviewing, mastery.State);
        // #237: one wrong + five correct observations; the cell machine keeps no streak counter, so
        // the count is the evidence rather than a "5 in a row" label.
        Assert.Equal(6, mastery.ObservationCount);
        Assert.Equal(0, mastery.ConsecutiveWrong);
        Assert.Equal(clock.GetUtcNow().AddDays(3), mastery.NextReviewDueAt);
    }

    // ---- Rule 6: two wrong in a row while Mastered lapses to Reviewing, +24h ----

    [Fact]
    public async Task Two_wrong_answers_in_a_row_lapse_a_mastered_category_back_to_reviewing()
    {
        var certCode = Unique("LP");
        var seeded = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        await DriveToMasteredAsync(practice, clock, userId, seeded.SectionId);

        var mastered = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(MasteryState.Mastered, mastered.State);
        Assert.Equal(clock.GetUtcNow().AddDays(14), mastered.NextReviewDueAt);
        Assert.NotNull(mastered.MasteredAt);

        clock.Advance(TimeSpan.FromDays(14).Add(TimeSpan.FromMinutes(1)));

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].ItemVersionId!.Value, Wrong(questions[0]));
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[1].ItemVersionId!.Value, Wrong(questions[1]));

        var lapsed = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(MasteryState.Reviewing, lapsed.State);
        Assert.Equal(clock.GetUtcNow().AddHours(24), lapsed.NextReviewDueAt);
        // Earned once and kept: a lapse does not rewrite the fact that it was reached.
        Assert.Equal(mastered.MasteredAt, lapsed.MasteredAt);
    }

    // ---- Rule 7: Mastered needs the stated proof, not merely a good run ----

    [Fact]
    public async Task A_category_reaches_mastered_only_on_85_percent_over_10_answers_across_two_days()
    {
        var certCode = Unique("MS");
        var seeded = await SeedCellAsync(certCode, questionCount: 12);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Ten correct answers - a full session - are not enough: the cell minimum is twelve
        // observations, so a good run stays at Reviewing. (#237 replaced the category machine's
        // "10 answers over 2 session days" with n_min = 12 objective observations.)
        var firstSession = await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);
        Assert.Equal(10, firstSession);

        var sameDay = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(MasteryState.Reviewing, sameDay.State);
        Assert.Equal(MasterySchedule.MinObservationsObjective - 2, sameDay.ObservationCount);
        Assert.Null(sameDay.MasteredAt);

        // A second session carries it over the line.
        clock.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(1)));
        await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);

        var secondDay = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(MasteryState.Mastered, secondDay.State);
        Assert.NotNull(secondDay.MasteredAt);
        Assert.Equal(clock.GetUtcNow().AddDays(14), secondDay.NextReviewDueAt);
    }

    /// <summary>Two sessions at 100% is the rule; this runs it and moves the clock between them,
    /// because a cell reviews after its spaced interval, not immediately.</summary>
    private static async Task DriveToMasteredAsync(PracticeService practice, ManualTimeProvider clock, int userId, int sectionId)
    {
        await AnswerSessionCorrectlyAsync(practice, userId, sectionId);
        clock.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(1)));
        await AnswerSessionCorrectlyAsync(practice, userId, sectionId);
    }

    // ---- Rule 8: a Smart Practice session is 10 questions, not a learner-chosen count ----

    [Fact]
    public async Task A_smart_session_is_ten_questions_and_records_that_mastery_drew_it()
    {
        var certCode = Unique("SP");
        var seeded = await SeedCellAsync(certCode, questionCount: 30);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Equal(10, session.QuestionCount);

        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        Assert.Equal(10, questions.Count);
        Assert.Equal(10, questions.Select(q => q.ItemVersionId).Distinct().Count());

        // With nothing ever answered, all ten are unseen - the ">= 6 unseen" floor of the mix - and
        // no evidence has been recorded for the cell.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Empty(await db.UserCellMasteries.Where(m => m.UserId == userId).ToListAsync());
        // #269: UserQuestionHistory went with the legacy question model, so there is no second ledger
        // to check here.
    }

    // ---- Rule 9: a session shorter than 10 is honest, not padded with duplicates ----

    [Fact]
    public async Task A_section_with_four_questions_serves_four_not_a_padded_ten()
    {
        var certCode = Unique("SM");
        var seeded = await SeedCellAsync(certCode, questionCount: 4);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Equal(4, session.QuestionCount);
        Assert.Equal(4, (await practice.GetSessionQuestionsAsync(userId, session.SessionId))
            .Select(q => q.ItemVersionId).Distinct().Count());
    }

    // ---- Rule 10: no AI call is made while answering ----

    [Fact]
    public async Task Answering_a_question_writes_no_ai_grading_and_no_grading_queue_row()
    {
        var certCode = Unique("NA");
        var seeded = await SeedCellAsync(certCode, questionCount: 3);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        foreach (var question in await practice.GetSessionQuestionsAsync(userId, session.SessionId))
        {
            await practice.SubmitAnswerAsync(userId, session.SessionId, question.ItemVersionId!.Value, Right(question));
        }

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Empty(await db.AiGradings.Where(g => g.FreeResponse.Attempt.UserId == userId).ToListAsync());
        // ...and the credit ledger, because practice stays free (AGENTS.md invariant #3).
        Assert.Empty(await db.CreditTransactions.Where(c => c.UserId == userId).ToListAsync());
    }

    // ---- Rule 12: an answered question cannot be answered again ----

    [Fact]
    public async Task Re_submitting_an_answer_is_refused_and_changes_neither_the_recorded_answer_nor_mastery()
    {
        var certCode = Unique("RS");
        var seeded = await SeedCellAsync(certCode, questionCount: 3);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        var question = questions[0];
        var wrongOption = Wrong(question).SelectedOptionId;
        var rightOption = Right(question).SelectedOptionId;

        await practice.SubmitAnswerAsync(userId, session.SessionId, question.ItemVersionId!.Value, Wrong(question));
        var once = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);

        // #166: #155 made the second grade skip mastery, so the re-submit rewrote the recorded answer
        // and the correct count while the mastery row kept describing the first answer. Posting a
        // wrong answer again as right also raised the accuracy a path step records. The question is
        // graded once, so the second submission is refused outright - not applied in part.
        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(userId, session.SessionId, question.ItemVersionId!.Value, Right(question)));

        Assert.Equal(PracticeException.AlreadyAnswered, ex.Code);
        // The message tells the learner what to do next, not what the server did.
        Assert.Contains("already been answered", ex.Message);
        Assert.Contains("next question", ex.Message);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.PracticeAnswers.AsNoTracking()
            .SingleAsync(a => a.SessionId == session.SessionId && a.ItemVersionId == question.ItemVersionId);

        // #269: a pool answer stores the LABEL it chose (an item version has no option rows to point
        // at), and the projection decodes it back to an option id. So the stored fact is "B", and the
        // id the learner saw is derived from it - both are asserted so neither can drift.
        Assert.Equal("B", stored.SelectedLabel);
        Assert.Null(stored.SelectedOptionId);
        Assert.Equal(question.Options.Single(o => o.Label == "B").OptionId, wrongOption);
        Assert.Equal(question.Options.Single(o => o.Label == "A").OptionId, rightOption);
        Assert.False(stored.IsCorrect);

        var sessionRow = await db.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
        Assert.Equal(0, sessionRow.CorrectCount);

        var twice = await ReadCellAsync(userId, seeded.TopicId, seeded.Level);
        Assert.Equal(1, once.ObservationCount);
        Assert.Equal(once.ObservationCount, twice.ObservationCount);
        Assert.Equal(once.NextReviewDueAt, twice.NextReviewDueAt);
    }

    // ---- Rule 11: the lesson micro-check writes the same mastery row a drill does ----

    [Fact]
    public async Task Finishing_a_lesson_micro_check_moves_its_category_to_learning()
    {
        var certCode = Unique("LM");
        var (certId, categoryId) = await SeedCategoryAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var lesson = await SeedLessonWithMicroCheckAsync(certId, categoryId);
        var answer = await practice.SubmitLessonMicroCheckAsync(
            userId, lesson.LessonId, lesson.MicroCheckCardId, lesson.WrongOption, default);

        Assert.False(answer.IsCorrect);
        Assert.Equal(lesson.RightOption, answer.CorrectOption);

        // #269: a micro-check is graded by SubmitLessonMicroCheckAsync, which moves the CATEGORY
        // axis (ApplyMasteryAsync). That is still the writer for a lesson, unchanged by the pool.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var mastery = await db.UserCategoryMasteries.AsNoTracking()
            .SingleAsync(m => m.UserId == userId && m.CategoryId == categoryId);

        Assert.Equal(MasteryState.Learning.ToCode(), mastery.State);
        Assert.Equal(1, mastery.AnsweredCount);
        Assert.Equal(clock.GetUtcNow().AddHours(24), mastery.NextReviewDueAt);
    }

    [Fact]
    public async Task Answering_the_same_micro_check_again_does_not_move_mastery_a_second_time()
    {
        // Found by review: unlike the drill, the micro-check had no "already graded" guard, so every
        // POST of the same card incremented AnsweredCount/Streak. A learner who learned the answer
        // could post it in a loop and farm the category to Mastered from one question.
        var certCode = Unique("LM2");
        var (certId, categoryId) = await SeedCategoryAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var lesson = await SeedLessonWithMicroCheckAsync(certId, categoryId);

        for (var i = 0; i < 5; i++)
        {
            await practice.SubmitLessonMicroCheckAsync(
                userId, lesson.LessonId, lesson.MicroCheckCardId, lesson.RightOption, default);
        }

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var mastery = await db.UserCategoryMasteries.AsNoTracking()
            .SingleAsync(m => m.UserId == userId && m.CategoryId == categoryId);

        // Five posts, one recorded answer.
        Assert.Equal(1, mastery.AnsweredCount);
        Assert.Equal(1, mastery.CorrectCount);
        Assert.Equal(MasteryState.Learning.ToCode(), mastery.State);
    }

    /// <summary>
    /// A certificate and one <c>QuestionCategory</c>, which is the axis a lesson's micro-check still
    /// grades against. No section and no questions: the lesson tests need neither, and a pool section
    /// would be stock no assertion here reads.
    /// </summary>
    private async Task<(int CertId, int CategoryId)> SeedCategoryAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var category = new QuestionCategory { CertId = cert.CertId, Code = "tense", Name = "Verb tense" };
        db.QuestionCategories.Add(category);
        await db.SaveChangesAsync();

        return (cert.CertId, category.CategoryId);
    }

    private async Task<(int LessonId, int MicroCheckCardId, string RightOption, string WrongOption)> SeedLessonWithMicroCheckAsync(int certId, int categoryId)
    {
        await SeedTopicsAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);

        // #240: a lesson is addressed by (skill, topic, level). The micro-check's answer is attributed
        // to the lesson sitting's own cell, so this fixture must sit on a real topic of the skill's
        // modality.
        var skill = await db.Skills.Where(s => s.CertId == certId).FirstAsync();

        var topicId = await db.Topics
            .Where(t => t.Modality == skill.Modality)
            .OrderBy(t => t.TopicId)
            .Select(t => t.TopicId)
            .FirstAsync();

        var lesson = new Lesson
        {
            CertId = certId,
            SkillId = skill.SkillId,
            Modality = skill.Modality,
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            CategoryId = categoryId,
            Title = "Tense drill",
            EstimatedMinutes = 5,
            DisplayOrder = 1,
        };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var card = new LessonCard
        {
            LessonId = lesson.LessonId,
            CertId = certId,
            CardType = "micro_check",
            Title = "One-question check",
            ContentMarkdown = "",
            QuestionPrompt = "Which is right?",
            OptionA = "Right",
            OptionB = "Wrong",
            CorrectOption = "A",
            Explanation = "Because.",
            DisplayOrder = 1,
        };
        db.LessonCards.Add(card);
        await db.SaveChangesAsync();
        return (lesson.LessonId, card.CardId, "A", "B");
    }
}
