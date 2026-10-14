using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SavedQuestionServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (SavedQuestionService SavedQuestions, PracticeService Practice, LearnerService Learner) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var practiceService = new PracticeService(db, learnerService);
        var savedQuestionService = new SavedQuestionService(db, learnerService);
        return (savedQuestionService, practiceService, learnerService);
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

    private async Task<(int CertId, int SectionId, List<int> QuestionIds, List<int> CorrectOptionIds, List<int> WrongOptionIds)> SeedCertificateWithQuestionsAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var cat = new QuestionCategory { CertId = cert.CertId, Code = Unique("CAT"), Name = "Grammar: Tenses" };
        db.QuestionCategories.Add(cat);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4 };
        db.Sections.Add(readSec);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var grp = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = readSec.SectionId, DisplayOrder = 1, Passage = "Sample Passage", Transcript = "Sample Transcript" };
        db.QuestionGroups.Add(grp);
        await db.SaveChangesAsync();

        var q1 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = readSec.SectionId, SkillId = skill.SkillId, CategoryId = cat.CategoryId, Stem = "Stem 1", DifficultyLevel = 3, QuestionType = "mcq", Hint = "Hint 1", Explanation = "Explanation 1", DisplayOrder = 1 };
        var q2 = new Question { GroupId = grp.GroupId, ExamId = exam.ExamId, SectionId = readSec.SectionId, SkillId = skill.SkillId, CategoryId = cat.CategoryId, Stem = "Stem 2", DifficultyLevel = 3, QuestionType = "mcq", Hint = "Hint 2", Explanation = "Explanation 2", DisplayOrder = 2 };
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

        return (cert.CertId, readSec.SectionId, qIds, correctOpts, wrongOpts);
    }

    [Fact]
    public async Task SaveQuestion_is_idempotent_when_called_multiple_times()
    {
        var certCode = Unique("SQ");
        var (_, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var qId = qIds[0];

        // First call saves question
        var res1 = await savedQuestions.SaveQuestionAsync(userId, qId, "manual");
        Assert.True(res1.IsSaved);
        Assert.Equal("manual", res1.Source);
        Assert.NotNull(res1.SavedAt);

        // Second call on same question succeeds idempotently without error
        var res2 = await savedQuestions.SaveQuestionAsync(userId, qId, "manual");
        Assert.True(res2.IsSaved);
        Assert.Equal("manual", res2.Source);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var rows = await db.SavedQuestions.Where(sq => sq.UserId == userId && sq.QuestionId == qId).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task UnsaveQuestion_is_idempotent_when_called_for_unsaved_question()
    {
        var certCode = Unique("SQ");
        var (_, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var qId = qIds[0];

        // Unsaving a question that is NOT currently saved succeeds idempotently
        var res1 = await savedQuestions.UnsaveQuestionAsync(userId, qId);
        Assert.False(res1.IsSaved);

        // Save it first
        await savedQuestions.SaveQuestionAsync(userId, qId, "manual");
        Assert.True(await savedQuestions.IsQuestionSavedAsync(userId, qId));

        // Unsave it
        var res2 = await savedQuestions.UnsaveQuestionAsync(userId, qId);
        Assert.False(res2.IsSaved);
        Assert.False(await savedQuestions.IsQuestionSavedAsync(userId, qId));

        // Second unsave succeeds idempotently
        var res3 = await savedQuestions.UnsaveQuestionAsync(userId, qId);
        Assert.False(res3.IsSaved);
    }

    [Fact]
    public async Task SaveQuestion_promotes_incorrect_source_to_manual_when_manually_saved()
    {
        var certCode = Unique("SQ");
        var (_, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var qId = qIds[0];

        // Initially saved as incorrect
        await savedQuestions.SaveQuestionAsync(userId, qId, "incorrect");
        var view1 = await savedQuestions.GetSavedQuestionAsync(userId, qId);
        Assert.Equal("incorrect", view1.Source);

        // Later learner explicitly saves it manually
        await savedQuestions.SaveQuestionAsync(userId, qId, "manual");
        var view2 = await savedQuestions.GetSavedQuestionAsync(userId, qId);
        Assert.Equal("manual", view2.Source);
    }

    [Fact]
    public async Task GetSavedQuestions_lists_only_learner_own_items_for_active_certificate()
    {
        var certA = Unique("CA");
        var certB = Unique("CB");
        var (_, _, qIdsA, _, _) = await SeedCertificateWithQuestionsAsync(certA);
        var (_, _, qIdsB, _, _) = await SeedCertificateWithQuestionsAsync(certB);

        var user1 = await SeedLearnerAsync(certA);
        var user2 = await SeedLearnerAsync(certA);
        var (savedQuestions, _, learners) = CreateServices();

        // User1 saves Q1 from CertA
        await savedQuestions.SaveQuestionAsync(user1, qIdsA[0], "manual");
        // User1 switches to CertB to save Q1 from CertB, then switches back to CertA
        await learners.SwitchCertificateAsync(user1, certB);
        await savedQuestions.SaveQuestionAsync(user1, qIdsB[0], "manual");
        await learners.SwitchCertificateAsync(user1, certA);

        // User2 saves Q2 from CertA
        await savedQuestions.SaveQuestionAsync(user2, qIdsA[1], "manual");

        // User1 viewing CertA should only see User1's question from CertA
        var viewUser1CertA = await savedQuestions.GetSavedQuestionsAsync(user1, new SavedQuestionFilterInput());
        Assert.Single(viewUser1CertA.Questions);
        Assert.Equal(qIdsA[0], viewUser1CertA.Questions[0].QuestionId);
        Assert.Equal(certA, viewUser1CertA.Certificate.Code);
        Assert.Equal(1, viewUser1CertA.TotalSavedCount);
        Assert.Equal(1, viewUser1CertA.ManualSavedCount);
        Assert.Equal(0, viewUser1CertA.IncorrectSavedCount);

        // User2 viewing CertA should only see User2's item
        var viewUser2CertA = await savedQuestions.GetSavedQuestionsAsync(user2, new SavedQuestionFilterInput());
        Assert.Single(viewUser2CertA.Questions);
        Assert.Equal(qIdsA[1], viewUser2CertA.Questions[0].QuestionId);

        // User1 switches to CertB
        await learners.SwitchCertificateAsync(user1, certB);
        var viewUser1CertB = await savedQuestions.GetSavedQuestionsAsync(user1, new SavedQuestionFilterInput());
        Assert.Single(viewUser1CertB.Questions);
        Assert.Equal(qIdsB[0], viewUser1CertB.Questions[0].QuestionId);
        Assert.Equal(certB, viewUser1CertB.Certificate.Code);
    }

    [Fact]
    public async Task GetSavedQuestions_filters_by_source_and_category()
    {
        var certCode = Unique("SQ");
        var (_, secId, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        await savedQuestions.SaveQuestionAsync(userId, qIds[0], "manual");
        await savedQuestions.SaveQuestionAsync(userId, qIds[1], "incorrect");

        // Filter by manual
        var manualOnly = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput(Source: "manual"));
        Assert.Single(manualOnly.Questions);
        Assert.Equal(qIds[0], manualOnly.Questions[0].QuestionId);
        Assert.Equal(2, manualOnly.TotalSavedCount); // Total overall for cert remains 2
        Assert.Equal(1, manualOnly.ManualSavedCount);
        Assert.Equal(1, manualOnly.IncorrectSavedCount);

        // Filter by incorrect
        var incorrectOnly = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput(Source: "incorrect"));
        Assert.Single(incorrectOnly.Questions);
        Assert.Equal(qIds[1], incorrectOnly.Questions[0].QuestionId);

        // Filter by section
        var bySection = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput(SectionId: secId));
        Assert.Equal(2, bySection.Questions.Count);
    }

    [Fact]
    public async Task GetSavedQuestion_returns_options_explanation_and_media()
    {
        var certCode = Unique("SQ");
        var (_, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var qId = qIds[0];

        await savedQuestions.SaveQuestionAsync(userId, qId, "manual");

        var item = await savedQuestions.GetSavedQuestionAsync(userId, qId);
        Assert.Equal(qId, item.QuestionId);
        Assert.Equal("Stem 1", item.Stem);
        Assert.Equal("Sample Passage", item.Passage);
        Assert.Equal("Explanation 1", item.Explanation);
        Assert.Equal("Sample Transcript", item.Transcript);
        Assert.Equal("Hint 1", item.Hint);
        Assert.Equal(2, item.Options.Count);
        Assert.Contains(item.Options, o => o.Label == "A" && o.IsCorrect);
        Assert.Contains(item.Options, o => o.Label == "B" && !o.IsCorrect);
    }

    [Fact]
    public async Task GetSavedQuestion_throws_when_not_saved_by_user()
    {
        var certCode = Unique("SQ");
        var (_, _, qIds, _, _) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.GetSavedQuestionAsync(userId, qIds[0]));
        Assert.Equal(SavedQuestionException.SavedQuestionNotFound, ex.Code);
    }

    [Fact]
    public async Task SubmitAnswer_automatically_saves_incorrect_mcq_to_saved_questions()
    {
        var certCode = Unique("SQ");
        var (_, secId, qIds, _, wrongOpts) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));
        var qId = qIds[0];

        // Submit wrong answer
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, qId, new SubmitPracticeAnswerInput(wrongOpts[0]));
        Assert.False(result.IsCorrect);
        Assert.True(result.IsSaved);

        // Verify question now exists in SavedQuestions with source = 'incorrect'
        var savedItem = await savedQuestions.GetSavedQuestionAsync(userId, qId);
        Assert.Equal(qId, savedItem.QuestionId);
        Assert.Equal("incorrect", savedItem.Source);
    }

    [Fact]
    public async Task SubmitAnswer_does_not_overwrite_manual_source_if_already_saved_manually()
    {
        var certCode = Unique("SQ");
        var (_, secId, qIds, _, wrongOpts) = await SeedCertificateWithQuestionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();
        var qId = qIds[0];

        // Manually save first
        await savedQuestions.SaveQuestionAsync(userId, qId, "manual");

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(secId, 1));

        // Submit wrong answer
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, qId, new SubmitPracticeAnswerInput(wrongOpts[0]));
        Assert.False(result.IsCorrect);
        Assert.True(result.IsSaved);

        // Source stays manual
        var savedItem = await savedQuestions.GetSavedQuestionAsync(userId, qId);
        Assert.Equal("manual", savedItem.Source);
    }

    [Fact]
    public async Task GetSavedQuestionAsync_throws_when_question_belongs_to_different_certificate_than_active()
    {
        var certCodeA = Unique("SQA");
        var certCodeB = Unique("SQB");
        var (_, _, qIdsA, _, _) = await SeedCertificateWithQuestionsAsync(certCodeA);
        await SeedCertificateWithQuestionsAsync(certCodeB);

        var userId = await SeedLearnerAsync(certCodeA);
        var (savedQuestions, _, learner) = CreateServices();
        var qIdA = qIdsA[0];

        // Save question while cert A is active
        await savedQuestions.SaveQuestionAsync(userId, qIdA, "manual");

        // Verify it loads when cert A is active
        var item = await savedQuestions.GetSavedQuestionAsync(userId, qIdA);
        Assert.Equal(qIdA, item.QuestionId);

        // Switch learner's active cert to cert B
        await learner.SwitchCertificateAsync(userId, certCodeB);

        // Certificate isolation: accessing cert A question while in cert B must throw 404 / not found
        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.GetSavedQuestionAsync(userId, qIdA));
        Assert.Equal(SavedQuestionException.SavedQuestionNotFound, ex.Code);
    }

    [Fact]
    public async Task SaveQuestionAsync_throws_when_question_belongs_to_different_certificate_than_active()
    {
        var certCodeA = Unique("SQA");
        var certCodeB = Unique("SQB");
        var (_, _, qIdsA, _, _) = await SeedCertificateWithQuestionsAsync(certCodeA);
        await SeedCertificateWithQuestionsAsync(certCodeB);

        var userId = await SeedLearnerAsync(certCodeB); // active cert is B
        var (savedQuestions, _, _) = CreateServices();
        var qIdA = qIdsA[0]; // question belongs to cert A

        // Saving a question from another certificate must be rejected
        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.SaveQuestionAsync(userId, qIdA, "manual"));
        Assert.Equal(SavedQuestionException.QuestionNotFound, ex.Code);
    }
}
