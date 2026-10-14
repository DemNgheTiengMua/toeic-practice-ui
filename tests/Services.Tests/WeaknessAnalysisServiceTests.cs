using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class WeaknessAnalysisServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (WeaknessAnalysisService WeaknessService, LearnerService Learner) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var weaknessService = new WeaknessAnalysisService(db, learnerService);
        return (weaknessService, learnerService);
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

    private async Task<(int CertId, int Sec1Id, int Sec2Id, int Cat1Id, int Cat2Id, List<int> QIds)> SeedCertificateWithCatalogAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var cat1 = new QuestionCategory { CertId = cert.CertId, Code = Unique("CAT1"), Name = "Grammar: Tenses" };
        var cat2 = new QuestionCategory { CertId = cert.CertId, Code = Unique("CAT2"), Name = "Vocabulary: Synonyms" };
        db.QuestionCategories.AddRange(cat1, cat2);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var sec1 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4 };
        var sec2 = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P6", Name = "Text Completion", DisplayOrder = 2, OptionCount = 4 };
        db.Sections.AddRange(sec1, sec2);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var grp1 = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec1.SectionId, DisplayOrder = 1 };
        var grp2 = new QuestionGroup { ExamId = exam.ExamId, CertId = cert.CertId, SectionId = sec2.SectionId, DisplayOrder = 2 };
        db.QuestionGroups.AddRange(grp1, grp2);
        await db.SaveChangesAsync();

        var q1 = new Question { GroupId = grp1.GroupId, ExamId = exam.ExamId, SectionId = sec1.SectionId, SkillId = skill.SkillId, CategoryId = cat1.CategoryId, Stem = "Q1 in P5", DifficultyLevel = 2, QuestionType = "mcq", DisplayOrder = 1 };
        var q2 = new Question { GroupId = grp1.GroupId, ExamId = exam.ExamId, SectionId = sec1.SectionId, SkillId = skill.SkillId, CategoryId = cat1.CategoryId, Stem = "Q2 in P5", DifficultyLevel = 2, QuestionType = "mcq", DisplayOrder = 2 };
        var q3 = new Question { GroupId = grp2.GroupId, ExamId = exam.ExamId, SectionId = sec2.SectionId, SkillId = skill.SkillId, CategoryId = cat2.CategoryId, Stem = "Q3 in P6", DifficultyLevel = 3, QuestionType = "mcq", DisplayOrder = 1 };
        var q4 = new Question { GroupId = grp2.GroupId, ExamId = exam.ExamId, SectionId = sec2.SectionId, SkillId = skill.SkillId, CategoryId = cat2.CategoryId, Stem = "Q4 in P6", DifficultyLevel = 3, QuestionType = "mcq", DisplayOrder = 2 };
        db.Questions.AddRange(q1, q2, q3, q4);
        await db.SaveChangesAsync();

        var opt1A = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "A", Text = "Correct A", IsCorrect = true };
        var opt1B = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong B", IsCorrect = false };

        var opt2A = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "A", Text = "Wrong A", IsCorrect = false };
        var opt2B = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "B", Text = "Correct B", IsCorrect = true };

        var opt3A = new QuestionOption { QuestionId = q3.QuestionId, QuestionType = "mcq", Label = "A", Text = "Wrong A", IsCorrect = false };
        var opt3B = new QuestionOption { QuestionId = q3.QuestionId, QuestionType = "mcq", Label = "B", Text = "Correct B", IsCorrect = true };

        var opt4A = new QuestionOption { QuestionId = q4.QuestionId, QuestionType = "mcq", Label = "A", Text = "Correct A", IsCorrect = true };
        var opt4B = new QuestionOption { QuestionId = q4.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong B", IsCorrect = false };

        db.QuestionOptions.AddRange(opt1A, opt1B, opt2A, opt2B, opt3A, opt3B, opt4A, opt4B);
        await db.SaveChangesAsync();

        return (cert.CertId, sec1.SectionId, sec2.SectionId, cat1.CategoryId, cat2.CategoryId, new List<int> { q1.QuestionId, q2.QuestionId, q3.QuestionId, q4.QuestionId });
    }

    [Fact]
    public async Task Empty_state_when_learner_has_no_practice_or_exam_answers()
    {
        var certCode = Unique("EMPTY");
        await SeedCertificateWithCatalogAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var view = await service.GetWeaknessAnalysisAsync(userId);

        Assert.False(view.HasData);
        Assert.Equal(0, view.TotalAnswered);
        Assert.Equal(0, view.TotalCorrect);
        Assert.Equal(0, view.GradedExamCount);
        Assert.Equal(0, view.CompletedPracticeCount);
        Assert.Empty(view.FrequentlyMissedCategories);
        Assert.Null(view.PriorityPracticeSection);
        Assert.All(view.Sections, s => Assert.Equal(0, s.Answered));
    }

    [Fact]
    public async Task Numbers_come_from_vw_UserSectionAccuracyAll_and_vw_UserCategoryAccuracy()
    {
        var certCode = Unique("ACCNUM");
        var (certId, sec1Id, sec2Id, cat1Id, cat2Id, qIds) = await SeedCertificateWithCatalogAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            // Practice session on Section 1 (P5): 2 questions answered, 1 correct, 1 incorrect
            var session = new PracticeSession
            {
                UserId = userId,
                SectionId = sec1Id,
                Modality = "reading",
                QuestionSource = "all",
                FeedbackMode = "immediate",
                Status = "finished",
                QuestionCount = 2,
                CorrectCount = 1,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                FinishedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            var pa1 = new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = qIds[0],
                SectionId = sec1Id,
                DisplayOrder = 1,
                IsCorrect = true
            };
            var pa2 = new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = qIds[1],
                SectionId = sec1Id,
                DisplayOrder = 2,
                IsCorrect = false
            };
            db.PracticeAnswers.AddRange(pa1, pa2);

            // Graded exam attempt on Section 2 (P6): 2 questions answered, both correct
            var examId = await db.Questions.Where(q => q.QuestionId == qIds[2]).Select(q => q.ExamId).SingleAsync();
            var attempt = new ExamAttempt
            {
                UserId = userId,
                CertId = certId,
                ExamId = examId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 90
            };
            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var aa1 = new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = examId,
                QuestionId = qIds[2],
                IsCorrect = true
            };
            var aa2 = new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = examId,
                QuestionId = qIds[3],
                IsCorrect = true
            };
            db.AttemptAnswers.AddRange(aa1, aa2);
            await db.SaveChangesAsync();
        }

        // Verify the SQL views directly in the DB
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var secView = await db.vw_UserSectionAccuracyAlls
                .Where(v => v.UserId == userId)
                .ToListAsync();
            Assert.Contains(secView, v => v.SectionId == sec1Id && v.Answered == 2 && v.Correct == 1);
            Assert.Contains(secView, v => v.SectionId == sec2Id && v.Answered == 2 && v.Correct == 2);

            var catView = await db.vw_UserCategoryAccuracies
                .Where(v => v.UserId == userId)
                .ToListAsync();
            Assert.Contains(catView, v => v.CategoryId == cat1Id && v.Answered == 2 && v.Correct == 1);
            Assert.Contains(catView, v => v.CategoryId == cat2Id && v.Answered == 2 && v.Correct == 2);
        }

        // Call weakness analysis service
        var view = await service.GetWeaknessAnalysisAsync(userId);

        Assert.True(view.HasData);
        Assert.Equal(4, view.TotalAnswered);
        Assert.Equal(3, view.TotalCorrect);
        Assert.Equal(75, view.OverallAccuracyPercentage);
        Assert.Equal(1, view.GradedExamCount);
        Assert.Equal(1, view.CompletedPracticeCount);

        // Section 1: 50% accuracy (weakest)
        var sec1Result = Assert.Single(view.Sections, s => s.SectionId == sec1Id);
        Assert.Equal(2, sec1Result.Answered);
        Assert.Equal(1, sec1Result.Correct);
        Assert.Equal(50, sec1Result.AccuracyPercentage);
        Assert.True(sec1Result.IsWeakest);
        Assert.False(sec1Result.IsStrongest);

        // Section 2: 100% accuracy (strongest)
        var sec2Result = Assert.Single(view.Sections, s => s.SectionId == sec2Id);
        Assert.Equal(2, sec2Result.Answered);
        Assert.Equal(2, sec2Result.Correct);
        Assert.Equal(100, sec2Result.AccuracyPercentage);
        Assert.False(sec2Result.IsWeakest);
        Assert.True(sec2Result.IsStrongest);

        // Priority practice is the weakest section
        Assert.NotNull(view.PriorityPracticeSection);
        Assert.Equal(sec1Id, view.PriorityPracticeSection!.SectionId);

        // Frequently missed categories: cat1 is 50%, cat2 is 100%
        Assert.Equal(2, view.FrequentlyMissedCategories.Count);
        var cat1Result = view.FrequentlyMissedCategories[0]; // weakest first
        Assert.Equal(cat1Id, cat1Result.CategoryId);
        Assert.Equal(50, cat1Result.AccuracyPercentage);
        Assert.Equal(2, cat1Result.Answered);
        Assert.Equal(1, cat1Result.Correct);
    }

    [Fact]
    public async Task Recommendations_can_be_marked_resolved_and_drop_off_the_open_list()
    {
        var certCode = Unique("RECO");
        var (certId, _, _, _, _, _) = await SeedCertificateWithCatalogAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        int rec1Id;
        int rec2Id;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r1 = new Recommendation
            {
                UserId = userId,
                CertId = certId,
                Text = "Review past continuous and present perfect.",
                Severity = "major",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var r2 = new Recommendation
            {
                UserId = userId,
                CertId = certId,
                Text = "Practice reading comprehension with inference questions.",
                Severity = "info",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Recommendations.AddRange(r1, r2);
            await db.SaveChangesAsync();
            rec1Id = r1.RecId;
            rec2Id = r2.RecId;
        }

        // Initially both recommendations are open and returned
        var viewBefore = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Equal(2, viewBefore.Recommendations.Count);
        Assert.Contains(viewBefore.Recommendations, r => r.RecId == rec1Id && !r.IsResolved);
        Assert.Contains(viewBefore.Recommendations, r => r.RecId == rec2Id && !r.IsResolved);

        // Mark rec1 as resolved
        var resolved = await service.ResolveRecommendationAsync(userId, rec1Id);
        Assert.True(resolved);

        // In DB, rec1 is now resolved
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r1Db = await db.Recommendations.FindAsync(rec1Id);
            Assert.NotNull(r1Db);
            Assert.True(r1Db!.IsResolved);
        }

        // Weakness analysis now drops rec1 off the open list!
        var viewAfter = await service.GetWeaknessAnalysisAsync(userId);
        Assert.Single(viewAfter.Recommendations);
        Assert.DoesNotContain(viewAfter.Recommendations, r => r.RecId == rec1Id);
        Assert.Contains(viewAfter.Recommendations, r => r.RecId == rec2Id);
    }

    [Fact]
    public async Task ResolveRecommendation_throws_when_recommendation_belongs_to_another_user()
    {
        var certCode = Unique("ISOL");
        var (certId, _, _, _, _, _) = await SeedCertificateWithCatalogAsync(certCode);
        var user1 = await SeedLearnerAsync(certCode);
        var user2 = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        int recId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var r = new Recommendation
            {
                UserId = user1,
                CertId = certId,
                Text = "User 1 recommendation",
                Severity = "minor",
                IsResolved = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Recommendations.Add(r);
            await db.SaveChangesAsync();
            recId = r.RecId;
        }

        // User 2 attempts to resolve User 1's recommendation
        await Assert.ThrowsAsync<RecommendationNotFoundException>(() =>
            service.ResolveRecommendationAsync(user2, recId));
    }
}
