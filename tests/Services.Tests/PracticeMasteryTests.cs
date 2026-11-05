using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #155: practice becomes real revision. One test per rule of the four-state mastery machine, one per
/// cooldown rule, and one for the 10-question Smart Practice mix - all against real SQL Server, real
/// triggers and the schema loaded from docs/database-schema.sql.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PracticeMasteryTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (PracticeService Practice, ManualTimeProvider Clock) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
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

    /// <summary>A certificate with one reading section holding one category of keyed questions. Passing
    /// a second category code seeds a second one in the same section, which is what proves the
    /// cooldown sends the learner elsewhere rather than refusing to start at all.</summary>
    private async Task<SeededCategory> SeedCategoryAsync(string certCode, int questionCount = 12, string? extraCategoryCode = null)
    {
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

        var category = new QuestionCategory { CertId = cert.CertId, Code = "tense", Name = "Verb tense" };
        db.QuestionCategories.Add(category);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 60, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var group = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec.SectionId, DisplayOrder = 1 };
        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync();

        var questionIds = new List<int>();
        for (var i = 0; i < questionCount; i++)
        {
            var question = new Question
            {
                GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = sec.SectionId, SkillId = skill.SkillId,
                CategoryId = category.CategoryId, Stem = $"Stem {i + 1}", DifficultyLevel = 3,
                QuestionType = "mcq", Explanation = $"Explanation {i + 1}", DisplayOrder = i + 1,
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            questionIds.Add(question.QuestionId);

            db.QuestionOptions.AddRange(
                new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true },
                new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false });
            await db.SaveChangesAsync();
        }

        var extraCategoryId = 0;
        var extraQuestionIds = new List<int>();
        if (extraCategoryCode is not null)
        {
            var extra = new QuestionCategory { CertId = cert.CertId, Code = extraCategoryCode, Name = "Preposition" };
            db.QuestionCategories.Add(extra);
            await db.SaveChangesAsync();
            extraCategoryId = extra.CategoryId;

            for (var i = 0; i < questionCount; i++)
            {
                var question = new Question
                {
                    GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = sec.SectionId, SkillId = skill.SkillId,
                    CategoryId = extra.CategoryId, Stem = $"Extra stem {i + 1}", DifficultyLevel = 3,
                    QuestionType = "mcq", Explanation = "Extra", DisplayOrder = 100 + i,
                };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                extraQuestionIds.Add(question.QuestionId);

                db.QuestionOptions.AddRange(
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true },
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false });
                await db.SaveChangesAsync();
            }
        }

        return new SeededCategory(cert.CertId, sec.SectionId, category.CategoryId, extraCategoryId, questionIds, extraQuestionIds);
    }

    private sealed record SeededCategory(
        int CertId, int SectionId, int CategoryId, int ExtraCategoryId, List<int> QuestionIds, List<int> ExtraQuestionIds);

    private async Task<UserCategoryMastery?> ReadMasteryAsync(int userId, int categoryId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.UserCategoryMasteries.AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId && m.CategoryId == categoryId);
    }

    private async Task<UserQuestionHistory?> ReadHistoryAsync(int userId, int questionId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.UserQuestionHistories.AsNoTracking()
            .SingleOrDefaultAsync(h => h.UserId == userId && h.QuestionId == questionId);
    }

    private static SubmitPracticeAnswerInput Right(PracticeQuestionView q) =>
        new(q.Options.Single(o => o.Text == "Right").OptionId);

    private static SubmitPracticeAnswerInput Wrong(PracticeQuestionView q) =>
        new(q.Options.Single(o => o.Text == "Wrong").OptionId);

    /// <summary>Starts one session and answers every question in it correctly. A session is where a
    /// learner actually meets a category, and the category is then rested until its next due date - so
    /// a test that wants more answers moves the clock forward, not starts another session.</summary>
    private static async Task<int> AnswerSessionCorrectlyAsync(PracticeService practice, int userId, int sectionId)
    {
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        foreach (var question in questions)
        {
            await practice.SubmitAnswerAsync(userId, session.SessionId, question.QuestionId!.Value, Right(question));
        }

        return questions.Count;
    }

    // ---- Rule 1: a wrong answer moves the category to Learning and sets a 24-hour review ----

    [Fact]
    public async Task A_wrong_answer_moves_the_category_to_learning_and_sets_a_24_hour_review()
    {
        var certCode = Unique("ML");
        var seeded = await SeedCategoryAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Untested is "no row yet": nothing is written before the first answer.
        Assert.Null(await ReadMasteryAsync(userId, seeded.CategoryId));

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].QuestionId!.Value, Wrong(questions[0]));

        var mastery = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.NotNull(mastery);
        Assert.Equal(MasteryState.Learning.ToCode(), mastery.State);
        Assert.Equal(1, mastery.AnsweredCount);
        Assert.Equal(0, mastery.CorrectCount);
        Assert.Equal(0, mastery.Streak);
        Assert.Equal(1, mastery.ConsecutiveWrong);
        Assert.Equal(clock.GetUtcNow().AddHours(24), mastery.NextReviewDueAt);
    }

    // ---- Rule 2: the question just answered wrongly is not served again during its cooldown ----

    [Fact]
    public async Task A_question_answered_wrongly_is_not_offered_again_before_its_cooldown_expires()
    {
        var certCode = Unique("QC");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 12, extraCategoryCode: "prep");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var first = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, first.SessionId);
        var missed = questions[0];
        await practice.SubmitAnswerAsync(userId, first.SessionId, missed.QuestionId!.Value, Wrong(missed));

        // Started straight away, so nothing has had time to become due again.
        var second = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var secondQuestions = await practice.GetSessionQuestionsAsync(userId, second.SessionId);
        Assert.DoesNotContain(missed.QuestionId, secondQuestions.Select(q => q.QuestionId));

        // Read the row too: a short session could pass the assertion above for the wrong reason.
        var history = await ReadHistoryAsync(userId, missed.QuestionId!.Value);
        Assert.NotNull(history);
        Assert.False(history.LastIsCorrect);
        Assert.Equal(seeded.CertId, await CertIdOfAsync(seeded.SectionId));
    }

    private async Task<int> CertIdOfAsync(int sectionId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Sections.Where(s => s.SectionId == sectionId).Select(s => s.CertId).SingleAsync();
    }

    // ---- Rule 3: 24 hours later the same question comes back ----

    [Fact]
    public async Task A_question_returns_after_its_24_hour_cooldown_expires()
    {
        var certCode = Unique("QR");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 12, extraCategoryCode: "prep");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var first = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, first.SessionId);
        var missed = questions[0];
        await practice.SubmitAnswerAsync(userId, first.SessionId, missed.QuestionId!.Value, Wrong(missed));

        var during = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.DoesNotContain(missed.QuestionId,
            (await practice.GetSessionQuestionsAsync(userId, during.SessionId)).Select(q => q.QuestionId));

        clock.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));

        // Every question in the section has been answered by now; the oldest-answered fallback is what
        // serves the lapsed question again, so the test does not depend on unseen stock remaining.
        var after = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Contains(missed.QuestionId,
            (await practice.GetSessionQuestionsAsync(userId, after.SessionId)).Select(q => q.QuestionId));
    }

    // ---- Rule 4: a category in cooldown is not chosen while a due category exists ----

    [Fact]
    public async Task A_category_in_cooldown_is_passed_over_for_a_due_category_and_says_when_it_returns()
    {
        var certCode = Unique("CC");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 12, extraCategoryCode: "prep");
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Put "Verb tense" into cooldown and leave "Preposition" untouched.
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        var tenseQuestion = questions.First(q => seeded.QuestionIds.Contains(q.QuestionId!.Value));
        await practice.SubmitAnswerAsync(userId, session.SessionId, tenseQuestion.QuestionId!.Value, Wrong(tenseQuestion));

        var cooling = (await practice.GetPracticeSectionsAsync(userId)).ReadingSections.Single(s => s.SectionId == seeded.SectionId);
        Assert.Equal(1, cooling.CategoriesInCooldown);
        Assert.Equal(clock.GetUtcNow().AddHours(24), cooling.NextReviewDueAt);

        var next = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.DoesNotContain(tenseQuestion.QuestionId,
            (await practice.GetSessionQuestionsAsync(userId, next.SessionId)).Select(q => q.QuestionId));

        clock.Advance(TimeSpan.FromHours(25));

        var due = (await practice.GetPracticeSectionsAsync(userId)).ReadingSections.Single(s => s.SectionId == seeded.SectionId);
        Assert.Equal(0, due.CategoriesInCooldown);
    }

    // ---- Rule 5: three correct in a row (five here) takes Learning to Reviewing, spaced +3 days ----

    [Fact]
    public async Task Five_correct_answers_in_a_row_move_the_category_from_learning_to_reviewing()
    {
        var certCode = Unique("LC");
        // Five questions, so the whole run stays under the ten answers Mastered needs: this test is
        // about the Reviewing rule, and a rule that fires first would hide it.
        var seeded = await SeedCategoryAsync(certCode, questionCount: 5);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].QuestionId!.Value, Wrong(questions[0]));
        Assert.Equal(MasteryState.Learning.ToCode(), (await ReadMasteryAsync(userId, seeded.CategoryId))!.State);

        // The category is in a 24-hour cooldown, so the clock has to reach the next day - which is also
        // the second session day the Mastered rule will later need.
        clock.Advance(TimeSpan.FromHours(25));
        await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);

        var mastery = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.NotNull(mastery);
        Assert.Equal(MasteryState.Reviewing.ToCode(), mastery.State);
        Assert.Equal(5, mastery.Streak);
        Assert.Equal(0, mastery.ConsecutiveWrong);
        Assert.Equal(clock.GetUtcNow().AddDays(3), mastery.NextReviewDueAt);
    }

    // ---- Rule 6: two wrong in a row while Mastered lapses to Reviewing, +24h ----

    [Fact]
    public async Task Two_wrong_answers_in_a_row_lapse_a_mastered_category_back_to_reviewing()
    {
        var certCode = Unique("LP");
        var seeded = await SeedCategoryAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        await DriveToMasteredAsync(practice, clock, userId, seeded.SectionId);

        var mastered = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.Equal(MasteryState.Mastered.ToCode(), mastered!.State);
        Assert.Equal(clock.GetUtcNow().AddDays(14), mastered.NextReviewDueAt);
        Assert.NotNull(mastered.MasteredAt);

        clock.Advance(TimeSpan.FromDays(14).Add(TimeSpan.FromMinutes(1)));

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[0].QuestionId!.Value, Wrong(questions[0]));
        await practice.SubmitAnswerAsync(userId, session.SessionId, questions[1].QuestionId!.Value, Wrong(questions[1]));

        var lapsed = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.Equal(MasteryState.Reviewing.ToCode(), lapsed!.State);
        Assert.Equal(clock.GetUtcNow().AddHours(24), lapsed.NextReviewDueAt);
        // Earned once and kept: a lapse does not rewrite the fact that it was reached.
        Assert.Equal(mastered.MasteredAt, lapsed.MasteredAt);
    }

    // ---- Rule 7: Mastered needs the stated proof, not merely a good run ----

    [Fact]
    public async Task A_category_reaches_mastered_only_on_85_percent_over_10_answers_across_two_days()
    {
        var certCode = Unique("MS");
        var seeded = await SeedCategoryAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        // Same day, 10 answers, 100%: Reviewing, never Mastered - one day is not two.
        await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);
        var sameDay = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.Equal(MasteryState.Reviewing.ToCode(), sameDay!.State);
        Assert.Equal(10, sameDay.AnsweredCount);
        Assert.Null(sameDay.MasteredAt);

        // A second day of correct answers carries it over the line.
        clock.Advance(TimeSpan.FromDays(3).Add(TimeSpan.FromMinutes(1)));
        await AnswerSessionCorrectlyAsync(practice, userId, seeded.SectionId);

        var secondDay = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.Equal(MasteryState.Mastered.ToCode(), secondDay!.State);
        Assert.NotNull(secondDay.MasteredAt);
        Assert.Equal(clock.GetUtcNow().AddDays(14), secondDay.NextReviewDueAt);
    }

    /// <summary>Two days of answers at 100% over ten each is the rule; this runs it and moves the clock
    /// between the days, because a category reviews after 24h, not immediately.</summary>
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
        var seeded = await SeedCategoryAsync(certCode, questionCount: 30);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Equal(10, session.QuestionCount);

        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        Assert.Equal(10, questions.Count);
        Assert.Equal(10, questions.Select(q => q.QuestionId).Distinct().Count());

        // With nothing ever answered, all ten are unseen - the ">= 6 unseen" floor of the mix.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Empty(await db.UserQuestionHistories.Where(h => h.UserId == userId).ToListAsync());
    }

    // ---- Rule 9: a session shorter than 10 is honest, not padded with duplicates ----

    [Fact]
    public async Task A_section_with_four_questions_serves_four_not_a_padded_ten()
    {
        var certCode = Unique("SM");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 4);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        Assert.Equal(4, session.QuestionCount);
        Assert.Equal(4, (await practice.GetSessionQuestionsAsync(userId, session.SessionId))
            .Select(q => q.QuestionId).Distinct().Count());
    }

    // ---- Rule 10: no AI call is made while answering ----

    [Fact]
    public async Task Answering_a_question_writes_no_ai_grading_and_no_grading_queue_row()
    {
        var certCode = Unique("NA");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 3);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        foreach (var question in await practice.GetSessionQuestionsAsync(userId, session.SessionId))
        {
            await practice.SubmitAnswerAsync(userId, session.SessionId, question.QuestionId!.Value, Right(question));
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
        var seeded = await SeedCategoryAsync(certCode, questionCount: 3);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(seeded.SectionId));
        var questions = await practice.GetSessionQuestionsAsync(userId, session.SessionId);
        var question = questions[0];
        var wrongOption = Wrong(question).SelectedOptionId;
        var rightOption = Right(question).SelectedOptionId;

        await practice.SubmitAnswerAsync(userId, session.SessionId, question.QuestionId!.Value, Wrong(question));
        var once = await ReadMasteryAsync(userId, seeded.CategoryId);

        // #166: #155 made the second grade skip mastery, so the re-submit rewrote the recorded answer
        // and the correct count while the mastery row kept describing the first answer. Posting a
        // wrong answer again as right also raised the accuracy a path step records. The question is
        // graded once, so the second submission is refused outright - not applied in part.
        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(userId, session.SessionId, question.QuestionId!.Value, Right(question)));

        Assert.Equal(PracticeException.AlreadyAnswered, ex.Code);
        // The message tells the learner what to do next, not what the server did.
        Assert.Contains("already been answered", ex.Message);
        Assert.Contains("next question", ex.Message);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.PracticeAnswers.AsNoTracking()
            .SingleAsync(a => a.SessionId == session.SessionId && a.QuestionId == question.QuestionId);
        Assert.Equal(wrongOption, stored.SelectedOptionId);
        Assert.NotEqual(rightOption, stored.SelectedOptionId);
        Assert.False(stored.IsCorrect);

        var sessionRow = await db.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
        Assert.Equal(0, sessionRow.CorrectCount);

        var twice = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.Equal(1, once!.AnsweredCount);
        Assert.Equal(once.AnsweredCount, twice!.AnsweredCount);
        Assert.Equal(once.NextReviewDueAt, twice.NextReviewDueAt);
    }

    // ---- Rule 11: the lesson micro-check writes the same mastery row a drill does ----

    [Fact]
    public async Task Finishing_a_lesson_micro_check_moves_its_category_to_learning()
    {
        var certCode = Unique("LM");
        var seeded = await SeedCategoryAsync(certCode, questionCount: 2);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, clock) = CreateServices();

        var lesson = await SeedLessonWithMicroCheckAsync(seeded.CertId, seeded.CategoryId);
        var answer = await practice.SubmitLessonMicroCheckAsync(
            userId, lesson.LessonId, lesson.MicroCheckCardId, lesson.WrongOption, default);

        Assert.False(answer.IsCorrect);
        Assert.Equal(lesson.RightOption, answer.CorrectOption);

        var mastery = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.NotNull(mastery);
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
        var seeded = await SeedCategoryAsync(certCode, questionCount: 2);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var lesson = await SeedLessonWithMicroCheckAsync(seeded.CertId, seeded.CategoryId);

        for (var i = 0; i < 5; i++)
        {
            await practice.SubmitLessonMicroCheckAsync(
                userId, lesson.LessonId, lesson.MicroCheckCardId, lesson.RightOption, default);
        }

        var mastery = await ReadMasteryAsync(userId, seeded.CategoryId);
        Assert.NotNull(mastery);
        // Five posts, one recorded answer.
        Assert.Equal(1, mastery.AnsweredCount);
        Assert.Equal(1, mastery.CorrectCount);
        Assert.Equal(MasteryState.Learning.ToCode(), mastery.State);
    }

    private async Task<(int LessonId, int MicroCheckCardId, string RightOption, string WrongOption)> SeedLessonWithMicroCheckAsync(int certId, int categoryId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var lesson = new Lesson
        {
            CertId = certId,
            SkillId = await db.Skills.Where(s => s.CertId == certId).Select(s => s.SkillId).FirstAsync(),
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
