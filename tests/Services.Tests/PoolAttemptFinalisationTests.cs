using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PoolAttemptFinalisationTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private async Task<int> AuthorAsync(string certCode)
    {
        await using var db = Db();
        var role = new Role { Code = "auth", Name = "Author" };
        if (!await db.Roles.AnyAsync(r => r.Code == "auth"))
        {
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var authorRole = await db.Roles.SingleAsync(r => r.Code == "auth");
        var user = new User { RoleId = authorRole.RoleId, Email = $"a-{certCode}@test.local", PasswordHash = [0x01] };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>
    /// #269: a MOCK attempt containing a POOL OBJECTIVE answer can be finalised after AI grading.
    /// <para>
    /// <c>FinalizeAiGradedAttemptAsync</c> built its legacy options lookup with
    /// <c>answer.Question.QuestionOptionQuestionNavigations</c> for every objective answer. A pool answer
    /// has no Question row, so that dereference threw NullReferenceException and finalising failed - the
    /// grading had already run and the credit was already taken, so the learner had a scored response and
    /// no result.
    /// </para>
    /// <para>
    /// <b>Two things make this test load-bearing, and the first two drafts missed both.</b> (a) The loop
    /// only runs for OBJECTIVE answers - a paper whose only answers are productive skips it, so an attempt
    /// built from one graded task proved nothing. (b) The method refuses any purpose that is not mock or
    /// placement, and returns early for an already-graded attempt, so finalising through the writing
    /// service was a no-op. This drives a MOCK paper that delivers both shapes, which is the real
    /// failing case.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_mock_attempt_with_a_pool_objective_answer_can_be_finalised_after_grading()
    {
        var certCode = Unique("FA3");
        var userId = await AuthorAsync(certCode);
        int attemptId;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var readSkill = new Skill
            {
                CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
                DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1, DurationMinutes = 30,
            };
            var writeSkill = new Skill
            {
                CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing",
                DisplayOrder = 2, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1, DurationMinutes = 30,
            };
            db.Skills.AddRange(readSkill, writeSkill);
            await db.SaveChangesAsync();

            var readSection = new Section
            {
                CertId = cert.CertId, SkillId = readSkill.SkillId, Modality = "reading",
                Code = "R1", Name = "Incomplete Sentences", DisplayOrder = 1,
                ExpectedQuestionCount = 1, LastWrittenBy = userId,
            };
            var writeSection = new Section
            {
                CertId = cert.CertId, SkillId = writeSkill.SkillId, Modality = "writing",
                Code = "W1", Name = "Sentence from picture", DisplayOrder = 2,
                ExpectedQuestionCount = 1, LastWrittenBy = userId,
            };
            db.Sections.AddRange(readSection, writeSection);
            await db.SaveChangesAsync();

            db.GradingCriteria.Add(new GradingCriterion
            {
                SkillId = writeSkill.SkillId, CertId = cert.CertId, Code = "W_TASK", Name = "Task achievement",
                MaxScore = 3m, Weight = 1m, DisplayOrder = 1,
                RubricDescription = "How fully the task is achieved.",
            });
            await db.SaveChangesAsync();

            db.ScoreBands.Add(new ScoreBand
            {
                CertId = cert.CertId, Code = "B1", Name = "Threshold", MinTotal = 0, MaxTotal = 990, DisplayOrder = 1,
            });
            await db.SaveChangesAsync();

            // A MOCK: the only purpose besides placement that this method will finalise.
            var exam = new Exam
            {
                CertId = cert.CertId, Name = $"Mock {certCode}", Status = "published",
                DurationMinutes = 60, Purpose = "mock", QuestionCount = 2,
                LastWrittenBy = userId,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            var readPosition = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, readSection.SectionId, "reading", "Pool reading Q", displayOrder: 1);
            var writePosition = await PoolFixture.AddProductiveAsync(
                db, exam.ExamId, cert.CertId, writeSection.SectionId, "writing",
                "Write a sentence about the picture.", displayOrder: 1, minWords: 5, maxWords: 40);

            db.Users.First(u => u.UserId == userId).ActiveCertId = cert.CertId;
            await db.SaveChangesAsync();

            var adminRole = await db.Roles.SingleAsync(r => r.Code == "auth");
            var admin = new User { RoleId = adminRole.RoleId, Email = $"admin-{certCode}@test.local", PasswordHash = [0x01] };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId, Reason = "admin_grant", Delta = 5,
                AdminUserId = admin.UserId, Note = "test credits", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var engine = new AttemptEngine(db, new LocalFileStore(Path.Combine(Path.GetTempPath(), "ee-" + certCode)));
            var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
            attemptId = attempt.AttemptId;

            // The objective pool answer whose Question navigation is null in the finalise loop.
            var keyOptionId = await PoolFixture.OptionIdAsync(db, exam.ExamId, readPosition.ExamItemId, "A");
            await engine.SaveAnswerAsync(attemptId, userId, readPosition.ExamItemId, keyOptionId, isFlagged: false);

            // The productive task's response row. CreateAttemptAsync already seeds one per productive
            // position, so write into it rather than inserting a second (UX_FreeResponses_ExamItem
            // refuses that). Written directly rather than through SaveDraftAsync, which refuses a
            // position outside the sitting's ACTIVE skill - the sitting is still in reading, and advancing
            // phases is not what this test is about.
            var seeded = await db.FreeResponses.SingleAsync(r => r.AttemptId == attemptId
                && r.ExamItemId == writePosition.ExamItemId);
            seeded.ResponseText = "The team is discussing the new proposal in the meeting room.";
            seeded.Status = FreeResponseStatus.Draft.ToCode();
            seeded.DraftSavedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await engine.SubmitAttemptAsync(attemptId, userId, isAutoSubmit: false);

            Assert.Empty(await db.Questions.Where(q => q.ExamId == exam.ExamId).ToListAsync());
        }

        // Grade and finalise. The processor finalises the attempt itself once every grading settles,
        // which is exactly the pass that used to throw on the pool objective answer.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            await new AiGradingProcessor(db, new CustomScoreGrader(2.0m)).ProcessPendingGradingsAsync();
        }

        await using var read = DbContextFactory.Create(database.ConnectionString);
        var done = await read.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal(AttemptStatus.Graded.ToCode(), done.Status);
        Assert.NotEmpty(await read.AttemptSkillScores.Where(s => s.AttemptId == attemptId).ToListAsync());
    }

    /// <summary>A grader that always returns the same mark, so the test does not depend on a rubric.</summary>
    private sealed class CustomScoreGrader(decimal fixedScore) : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            var scores = request.Criteria
                .Select(c => new AiCriterionScoreResult(c.CriterionId, fixedScore, "Overridden score"))
                .ToList();
            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: "custom-grader",
                Scores: scores));
        }
    }
}
