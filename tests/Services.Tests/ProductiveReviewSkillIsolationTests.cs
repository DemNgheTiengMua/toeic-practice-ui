using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// One paper, two productive skills. A placement carries Writing and Speaking on a single attempt, and
/// each result page must show only its own skill.
/// <para>
/// <b>What shipped instead, seen in a browser on 2026-11-15:</b> the speaking result page listed the
/// writing task under a heading that reads "Pronunciation &amp; rubric criteria", and its header said
/// <c>"2 of 1 tasks answered"</c> — because <c>totalExamQuestions</c> counts speaking positions while
/// the loop counted every free response on the attempt.
/// </para>
/// <para>
/// <b>Why a shared-paper fixture and not two single-skill papers:</b> a single-skill paper passes the
/// old code, because the filter has nothing to exclude. The defect only exists when one attempt holds
/// both, which is exactly the placement shape.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductiveReviewSkillIsolationTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>A certificate with a writing skill and a speaking skill, and one exam holding both.</summary>
    private async Task<(int CertId, int ExamId, int WritingItemId, int SpeakingItemId)> SeedSharedPaperAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        db.Skills.AddRange(
            new Skill { CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 1 },
            new Skill { CertId = cert.CertId, Code = "SPEA", Name = "Speaking", Modality = "speaking", DisplayOrder = 2 });
        await db.SaveChangesAsync();

        var writingSkill = await db.Skills.SingleAsync(s => s.CertId == cert.CertId && s.Code == "WRIT");
        var speakingSkill = await db.Skills.SingleAsync(s => s.CertId == cert.CertId && s.Code == "SPEA");

        var writingSection = new Section
        {
            SkillId = writingSkill.SkillId, CertId = cert.CertId, Modality = "writing", Code = "W1",
            Name = "Writing task", ExpectedQuestionCount = 1, DisplayOrder = 1,
        };
        var speakingSection = new Section
        {
            SkillId = speakingSkill.SkillId, CertId = cert.CertId, Modality = "speaking", Code = "S1",
            Name = "Speaking task", ExpectedQuestionCount = 1, DisplayOrder = 2,
        };
        db.Sections.AddRange(writingSection, speakingSection);
        await db.SaveChangesAsync();

        db.GradingCriteria.AddRange(
            new GradingCriterion
            {
                CertId = cert.CertId, SkillId = writingSkill.SkillId, Code = "CC", Name = "Coherence",
                RubricDescription = "Band 5: organised. Band 3: partly organised.",
                MaxScore = 5.0m, Weight = 1.0m, DisplayOrder = 1,
            },
            new GradingCriterion
            {
                CertId = cert.CertId, SkillId = speakingSkill.SkillId, Code = "PRON", Name = "Pronunciation",
                RubricDescription = "Band 5: clear. Band 3: intrusive errors.",
                MaxScore = 5.0m, Weight = 1.0m, DisplayOrder = 1,
            });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Shared Paper {certCode}", Status = "published",
            DurationMinutes = 60, Purpose = "mock", QuestionCount = 2,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var writing = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, writingSection.SectionId, "writing",
            "Write an essay about remote work.", displayOrder: 1, minWords: 5, maxWords: 300, level: "B1");
        var speaking = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, speakingSection.SectionId, "speaking",
            "Do you work or are you a student?", displayOrder: 2, responseSeconds: 60, level: "B1");

        return (cert.CertId, exam.ExamId, writing.ExamItemId, speaking.ExamItemId);
    }

    private async Task<int> SeedLearnerAsync(int certId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        if (!await db.Roles.AnyAsync(r => r.Code == "learner"))
        {
            db.Roles.Add(new Role { Code = "learner", Name = "Learner" });
            await db.SaveChangesAsync();
        }
        if (!await db.Roles.AnyAsync(r => r.Code == "admin"))
        {
            db.Roles.Add(new Role { Code = "admin", Name = "Admin" });
            await db.SaveChangesAsync();
        }

        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).SingleAsync();
        var adminRoleId = await db.Roles.Where(r => r.Code == "admin").Select(r => r.RoleId).SingleAsync();

        var email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = roleId, Email = email, PasswordHash = [0x01], ActiveCertId = certId });
        var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = adminRoleId, Email = adminEmail, PasswordHash = [0x01] });
        await db.SaveChangesAsync();

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.UserId).SingleAsync();

        // Grading both productive skills charges one credit each; a ledger grant keeps the submit path
        // reachable so the test measures the review page rather than the credit gate.
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = userId,
            Reason = "admin_grant",
            Delta = 4,
            AdminUserId = adminId,
            Note = "Test setup credits",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return userId;
    }

    /// <summary>
    /// Writes both responses through the service's own submit path, then lets the worker grade them.
    /// Using the real submit is what moves the attempt to `submitted` and enqueues both gradings; a
    /// hand-written UPDATE skipped that and left the attempt ungradeable.
    /// </summary>
    private async Task SitBothAsync(int userId, int attemptId, int writingItemId, int speakingItemId)
    {
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var store = new LocalFileStore(Path.Combine(Path.GetTempPath(), "ee_iso_" + Guid.NewGuid().ToString("N")));
            var writing = new WritingPracticeService(db, new LearnerService(db));
            await writing.SaveDraftAsync(userId, attemptId, writingItemId,
                "Working from home gives employees greater flexibility over their schedules and removes "
                + "the daily commute, which saves both time and money. It also lets people focus on "
                + "deep work without the interruptions of an open office.");

            // A speaking response is an uploaded file, not a draft; the service validates and stores it.
            var speaking = new SpeakingPracticeService(db, new LearnerService(db), store);
            using var audio = new MemoryStream(new byte[2048]);
            await speaking.SaveAudioAsync(userId, attemptId, speakingItemId, audio, "audio/webm", "q.webm", 30_000);
        }

        // Both services share one attempt; the writing submit closes the paper's productive half.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var writing = new WritingPracticeService(db, new LearnerService(db));
            await writing.SubmitAttemptAsync(userId, attemptId);
        }

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var processor = new AiGradingProcessor(db, new FakeAiGrader());
            await processor.ProcessPendingGradingsAsync();
        }
    }

    [Fact]
    public async Task The_speaking_result_lists_only_the_speaking_task_and_counts_it_once()
    {
        var certCode = Unique("ISO1");
        var (certId, examId, writingItemId, speakingItemId) = await SeedSharedPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var engine = new AttemptEngine(DbContextFactory.Create(database.ConnectionString));
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await SitBothAsync(userId, attempt.AttemptId, writingItemId, speakingItemId);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var store = new LocalFileStore(Path.Combine(Path.GetTempPath(), "ee_iso_" + Guid.NewGuid().ToString("N")));
        var service = new SpeakingPracticeService(verifyDb, new LearnerService(verifyDb), store);
        var review = await service.GetAttemptReviewAsync(userId, attempt.AttemptId);

        // The page is the SPEAKING result: one task, no writing task, and the header cannot say
        // "2 of 1" because totalQuestions and answered both describe the same filtered set.
        Assert.Single(review.Questions);
        Assert.Equal(1, review.TotalQuestions);
        Assert.Equal(1, review.AnsweredQuestions);
        Assert.Contains("Speaking", review.Questions[0].SectionName);
        Assert.DoesNotContain(review.Questions, q => q.SectionName.Contains("Writing"));
    }

    [Fact]
    public async Task The_writing_result_lists_only_the_writing_task_and_counts_it_once()
    {
        var certCode = Unique("ISO2");
        var (certId, examId, writingItemId, speakingItemId) = await SeedSharedPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var engine = new AttemptEngine(DbContextFactory.Create(database.ConnectionString));
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await SitBothAsync(userId, attempt.AttemptId, writingItemId, speakingItemId);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var service = new WritingPracticeService(verifyDb, new LearnerService(verifyDb));
        var review = await service.GetAttemptReviewAsync(userId, attempt.AttemptId);

        Assert.Single(review.Questions);
        Assert.Equal(1, review.TotalQuestions);
        Assert.Equal(1, review.AnsweredQuestions);
        Assert.DoesNotContain(review.Questions, q => q.SectionName.Contains("Speaking"));
    }
}
