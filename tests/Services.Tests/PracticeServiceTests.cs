using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
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

    private async Task<(int CertId, int SectionId, int SpeakingSecId, List<int> QuestionIds, List<int> CorrectOptionIds, List<int> WrongOptionIds)> SeedCertificateWithQuestionsAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        var spkSkill = new Skill { CertId = cert.CertId, Code = "SPEA", Name = "Speaking", Modality = "speaking", DisplayOrder = 2 };
        db.Skills.AddRange(skill, spkSkill);
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4 };
        var spkSec = new Section { CertId = cert.CertId, SkillId = spkSkill.SkillId, Modality = "speaking", Code = "S1", Name = "Speaking Part 1", DisplayOrder = 1 };
        db.Sections.AddRange(readSec, spkSec);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = readSec.SectionId, DisplayOrder = 1 };
        db.QuestionGroups.Add(grp);
        await db.SaveChangesAsync();

        var q1 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = readSec.SectionId, SkillId = skill.SkillId, Stem = "Stem 1", DifficultyLevel = 3, QuestionType = "mcq", Hint = "Hint 1", Explanation = "Explanation 1", DisplayOrder = 1 };
        var q2 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = readSec.SectionId, SkillId = skill.SkillId, Stem = "Stem 2", DifficultyLevel = 3, QuestionType = "mcq", Hint = "Hint 2", Explanation = "Explanation 2", DisplayOrder = 2 };
        var q3 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = readSec.SectionId, SkillId = skill.SkillId, Stem = "Stem 3", DifficultyLevel = 3, QuestionType = "mcq", Hint = "Hint 3", Explanation = "Explanation 3", DisplayOrder = 3 };
        db.Questions.AddRange(q1, q2, q3);
        await db.SaveChangesAsync();

        var opt1A = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "A", Text = "Correct A", IsCorrect = true };
        var opt1B = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong B", IsCorrect = false };

        var opt2A = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "A", Text = "Wrong A", IsCorrect = false };
        var opt2B = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "B", Text = "Correct B", IsCorrect = true };

        var opt3A = new QuestionOption { QuestionId = q3.QuestionId, QuestionType = "mcq", Label = "A", Text = "Wrong A", IsCorrect = false };
        var opt3B = new QuestionOption { QuestionId = q3.QuestionId, QuestionType = "mcq", Label = "B", Text = "Correct B", IsCorrect = true };
        db.QuestionOptions.AddRange(opt1A, opt1B, opt2A, opt2B, opt3A, opt3B);
        await db.SaveChangesAsync();

        var qIds = new List<int> { q1.QuestionId, q2.QuestionId, q3.QuestionId };
        var optionsInDb = await db.QuestionOptions.Where(o => qIds.Contains(o.QuestionId)).ToListAsync();
        var correctOpts = qIds.Select(id => optionsInDb.First(o => o.QuestionId == id && o.IsCorrect).OptionId).ToList();
        var wrongOpts = qIds.Select(id => optionsInDb.First(o => o.QuestionId == id && !o.IsCorrect).OptionId).ToList();

        return (cert.CertId, readSec.SectionId, spkSec.SectionId, qIds, correctOpts, wrongOpts);
    }

    [Fact]
    public async Task GetPracticeSections_returns_sections_with_available_question_counts()
    {
        var certCode = Unique("PR");
        var (certId, readSecId, _, _, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var view = await practice.GetPracticeSectionsAsync(userId);

        Assert.Equal(certCode, view.Certificate.Code);
        Assert.Single(view.ReadingSections);
        Assert.Equal(3, view.ReadingSections[0].AvailableQuestionCount);
        Assert.Equal("Incomplete Sentences", view.ReadingSections[0].Name);
        Assert.Single(view.OtherSections);
    }

    [Fact]
    public async Task StartSession_refuses_speaking_section()
    {
        var certCode = Unique("SP");
        var (_, _, spkSecId, _, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.StartSessionAsync(userId, new StartPracticeSessionInput(spkSecId, 10)));

        Assert.Equal(PracticeException.ModalityNotAllowed, ex.Code);
    }

    [Fact]
    public async Task StartSession_refuses_section_from_different_certificate()
    {
        var certCode1 = Unique("C1");
        var certCode2 = Unique("C2");
        var (_, sec1Id, _, _, _, _) = await SeedCertificateWithQuestionsAsync(certCode1);
        await SeedCertificateWithQuestionsAsync(certCode2);
        var userId = await SeedLearnerAsync(certCode2); // User is active in cert 2
        var (practice, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.StartSessionAsync(userId, new StartPracticeSessionInput(sec1Id, 10)));

        Assert.Equal(PracticeException.SectionUnavailable, ex.Code);
    }

    [Fact]
    public async Task StartSession_creates_session_and_answers_for_mcq_questions_only()
    {
        var certCode = Unique("MC");
        var (_, secId, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

        Assert.Equal("in_progress", session.Status);
        Assert.Equal(2, session.QuestionCount);
        Assert.Equal(0, session.CorrectCount);

        var q1 = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.Equal(qIds[0], q1.QuestionId);
        Assert.Equal(1, q1.DisplayOrder);
        Assert.Equal(2, q1.TotalQuestions);
        Assert.False(q1.HintUsed);
        Assert.Null(q1.Hint); // Not revealed yet
        Assert.Null(q1.IsCorrect); // Not answered yet
        Assert.Null(q1.CorrectOptionId); // Cannot leak answer
    }

    [Fact]
    public async Task RevealHint_records_hint_used_on_practice_answer()
    {
        var certCode = Unique("HN");
        var (_, secId, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));
        var hint = await practice.RevealHintAsync(userId, session.SessionId, qIds[0]);

        Assert.Equal("Hint 1", hint);

        var q1 = await practice.GetQuestionAsync(userId, session.SessionId, 1);
        Assert.True(q1.HintUsed);
        Assert.Equal("Hint 1", q1.Hint);
    }

    [Fact]
    public async Task SubmitAnswer_grades_answer_immediately_and_shows_explanation()
    {
        var certCode = Unique("GR");
        var (_, secId, _, qIds, correctOpts, wrongOpts) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

        // Submit correct answer to Q1
        var result1 = await practice.SubmitAnswerAsync(
            userId, session.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[0]));

        Assert.True(result1.IsCorrect);
        Assert.Equal(correctOpts[0], result1.SelectedOptionId);
        Assert.Equal("Explanation 1", result1.Explanation);
        Assert.False(result1.IsSessionFinished);
        Assert.Equal(2, result1.NextQuestionDisplayOrder);

        // Submit wrong answer to Q2
        var result2 = await practice.SubmitAnswerAsync(
            userId, session.SessionId, qIds[1], new SubmitPracticeAnswerInput(wrongOpts[1]));

        Assert.False(result2.IsCorrect);
        Assert.Equal(wrongOpts[1], result2.SelectedOptionId);
        Assert.Equal(correctOpts[1], result2.CorrectOptionId);
        Assert.Equal("Explanation 2", result2.Explanation);
        Assert.True(result2.IsSessionFinished);
        Assert.Null(result2.NextQuestionDisplayOrder);

        var info = await practice.GetSessionInfoAsync(userId, session.SessionId);
        Assert.Equal("finished", info.Status);
        Assert.Equal(1, info.CorrectCount);
    }

    [Fact]
    public async Task SubmitAnswer_rejects_option_from_different_question()
    {
        var certCode = Unique("OP");
        var (_, secId, _, qIds, correctOpts, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

        // Try submitting Q2's option for Q1
        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(userId, session.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[1])));

        Assert.Equal(PracticeException.InvalidOption, ex.Code);
    }

    [Fact]
    public async Task GetSummary_computes_per_question_outcome_and_skip_accuracy_math()
    {
        var certCode = Unique("SM");
        var (_, secId, _, qIds, correctOpts, wrongOpts) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 3));

        // Q1: reveal hint and answer correctly
        await practice.RevealHintAsync(userId, session.SessionId, qIds[0]);
        await practice.SubmitAnswerAsync(userId, session.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[0]));

        // Q2: answer incorrectly without hint
        await practice.SubmitAnswerAsync(userId, session.SessionId, qIds[1], new SubmitPracticeAnswerInput(wrongOpts[1]));

        // Q3: skip (SelectedOptionId = null)
        await practice.SubmitAnswerAsync(userId, session.SessionId, qIds[2], new SubmitPracticeAnswerInput(null));

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
        Assert.False(summary.Questions[1].IsCorrect);
        Assert.False(summary.Questions[1].HintUsed);
        Assert.Null(summary.Questions[2].IsCorrect); // Skipped
    }

    [Fact]
    public async Task StartSession_respects_wrong_before_and_unseen_question_sources()
    {
        var certCode = Unique("QS");
        var (_, secId, _, qIds, correctOpts, wrongOpts) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // Initial session: answer Q1 wrongly
        var session1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));
        await practice.SubmitAnswerAsync(userId, session1.SessionId, qIds[0], new SubmitPracticeAnswerInput(wrongOpts[0]));

        // Session with wrong_before: only Q1 should be selected
        var sessionWrong = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 10, questionSource: "wrong_before"));
        Assert.Equal(1, sessionWrong.QuestionCount);
        var wrongQ = await practice.GetQuestionAsync(userId, sessionWrong.SessionId, 1);
        Assert.Equal(qIds[0], wrongQ.QuestionId);

        // Session with unseen: only Q2 and Q3 should be selected
        var sessionUnseen = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 10, questionSource: "unseen"));
        Assert.Equal(2, sessionUnseen.QuestionCount);
        var unseenQ1 = await practice.GetQuestionAsync(userId, sessionUnseen.SessionId, 1);
        var unseenQ2 = await practice.GetQuestionAsync(userId, sessionUnseen.SessionId, 2);
        Assert.Equal(qIds[1], unseenQ1.QuestionId);
        Assert.Equal(qIds[2], unseenQ2.QuestionId);
    }

    [Fact]
    public async Task StartSession_prunes_older_sessions_and_answer_logs_when_exceeding_limit()
    {
        var certCode = Unique("PR");
        var (_, secId, _, qIds, correctOpts, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();
        practice.MaxSavedSessionsPerCertificate = 2; // Test limit of 2

        // Session 1: complete it
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));
        await practice.SubmitAnswerAsync(userId, s1.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[0]));

        // Session 2: complete it
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));
        await practice.SubmitAnswerAsync(userId, s2.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[0]));

        // Session 3: start it -> exceeds limit of 2, so s1 and its answers should be pruned
        var s3 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));

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
        var (_, secId, _, _, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // Start session 1 but do not answer any questions
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

        // Start session 2 -> s1 was empty, so it should be cleaned up immediately
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

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
        var (_, secId, _, qIds, correctOpts, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        // Start session 1 and answer question 1
        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));
        await practice.SubmitAnswerAsync(userId, s1.SessionId, qIds[0], new SubmitPracticeAnswerInput(correctOpts[0]));

        // Start session 2 -> s1 had an answer, so it should be preserved as "abandoned"
        var s2 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

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
        var (_, secId, _, _, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (practice, _) = CreateServices();

        var s1 = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 2));

        var catalog = await practice.GetPracticeSectionsAsync(userId);

        Assert.NotNull(catalog.UnfinishedSession);
        Assert.Equal(s1.SessionId, catalog.UnfinishedSession.SessionId);
        Assert.Equal("in_progress", catalog.UnfinishedSession.Status);
        Assert.Equal(secId, catalog.UnfinishedSession.SectionId);
    }
}
