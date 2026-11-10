using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 / #262: <b>writing and speaking practice read the paper roster.</b>
/// <para>
/// Both services enumerated an exam's tasks with a legacy query. On a paper assembled from the pool that
/// query returns nothing, so the screen reported "no questions found" for a paper the engine can sit -
/// and both stamped <c>Attempts.LastQuestionId</c>, a column this epic retires.
/// </para>
/// <para>
/// <b>What this pins.</b> The precondition (zero question rows) is asserted first, because without it the
/// test would pass on a fixture that quietly left legacy content behind. The assertions are on what the
/// learner sees: the task's stem, its word limits, the navigator's length, the resumed draft.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolProductivePracticeTests(DatabaseFixture database)
{
    /// <summary>A pool-only writing paper can be started, taken, resumed and drafted.</summary>
    [Fact]
    public async Task Writing_practice_works_on_a_paper_whose_tasks_live_only_on_the_pool()
    {
        var name = $"EE262W_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedProductivePaperAsync(connectionString, "writing", "W-ARG");

            await using var db = DbContextFactory.Create(connectionString);

            Assert.Equal(0, await db.Questions.CountAsync(q => q.ExamId == fixture.ExamId));
            Assert.Equal(2, await db.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId));

            var service = new WritingPracticeService(db, new LearnerService(db));

            var started = await service.StartAttemptAsync(fixture.UserId, fixture.ExamId);
            Assert.True(started.AttemptId > 0);

            var take = await service.GetAttemptTakeAsync(fixture.UserId, started.AttemptId, questionId: null);
            Assert.Equal(2, take.TotalQuestions);
            Assert.Equal(1, take.CurrentQuestionIndex);
            Assert.StartsWith("Pool writing task", take.Question.Stem, StringComparison.Ordinal);
            Assert.Equal(150, take.Question.MinWords);

            // The navigator names the same two positions the paper declares.
            Assert.Equal(2, take.Questions.Count);
            Assert.False(take.Questions[0].IsAnswered);

            var saved = await service.SaveDraftAsync(
                fixture.UserId, started.AttemptId, take.Question.QuestionId, "A draft answer to the task.");
            Assert.True(saved.WordCount > 0);

            // Resume, with no position named, must land on the position the draft belongs to.
            var resumed = await service.GetAttemptTakeAsync(fixture.UserId, started.AttemptId, questionId: null);
            Assert.Equal("A draft answer to the task.", resumed.Question.ResponseText);
            Assert.True(resumed.Questions[0].IsAnswered);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>A pool-only speaking paper can be started and taken, with its reference resolved.</summary>
    [Fact]
    public async Task Speaking_practice_works_on_a_paper_whose_tasks_live_only_on_the_pool()
    {
        var name = $"EE262S_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedProductivePaperAsync(connectionString, "speaking", "S-DESC");

            await using var db = DbContextFactory.Create(connectionString);

            Assert.Equal(0, await db.Questions.CountAsync(q => q.ExamId == fixture.ExamId));
            Assert.Equal(2, await db.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId));

            var service = new SpeakingPracticeService(db, new LearnerService(db), new LocalFileStore(
                Path.Combine(Path.GetTempPath(), "ee262speaking")));

            var started = await service.StartAttemptAsync(fixture.UserId, fixture.ExamId);
            var take = await service.GetAttemptTakeAsync(fixture.UserId, started.AttemptId, questionId: null);

            Assert.Equal(2, take.TotalQuestions);
            Assert.Equal(1, take.CurrentQuestionIndex);
            Assert.StartsWith("Pool speaking task", take.Question.Stem, StringComparison.Ordinal);
            Assert.Equal(2, take.Questions.Count);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(int UserId, int CertId, int ExamId);

    /// <summary>
    /// A published practice paper whose two tasks exist only on the pool. No question group, no question
    /// row, no option row - the three tables #250 drops.
    /// </summary>
    private static async Task<Fixture> SeedProductivePaperAsync(
        string connectionString, string modality, string topicCode)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"P{modality[..2].ToUpperInvariant()}{Guid.NewGuid():N}"[..12];
        var purpose = modality == "writing" ? "writing_practice" : "speaking_practice";

        var cert = new Certificate { Code = code, Name = $"Pool {modality} cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId,
            Code = modality[..4].ToUpperInvariant(),
            Name = modality,
            Modality = modality,
            DisplayOrder = 1,
            DurationMinutes = 30,
            MinScaledScore = 0,
            MaxScaledScore = 9,
            ScoreStep = 1,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = modality,
            Code = "T1",
            Name = $"{modality} tasks",
            OptionCount = 0,
            ExpectedQuestionCount = 2,
            DisplayOrder = 1,
            NavigationMode = "FULL_SANDBOX",
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 4, DisplayOrder = 1,
        });
        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "HIGH", Name = "High", MinTotal = 5, MaxTotal = 9, DisplayOrder = 2,
        });
        await db.SaveChangesAsync();

        db.ScoreScales.Add(new ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 0, ScaledScore = 0,
        });
        db.ScoreScales.Add(new ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 2, ScaledScore = 6,
        });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId,
            Name = $"Pool {modality} practice {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = purpose,
            QuestionCount = 2,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // A canonical topic of this modality: the vocabulary is closed at twenty.
        var topic = new Topic { Modality = modality, Code = topicCode, Name = $"{modality} topic" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var itemType = modality == "writing" ? "persuasive_essay" : "descriptive_monologue";
        for (var i = 0; i < 2; i++)
        {
            var item = new Item
            {
                Modality = modality,
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B1,
                ItemType = itemType,
                RenderFormat = "free_response",
                ContentTier = "demo",
                LifecycleStatus = "active",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Pool {modality} task {i + 1}: respond to the prompt.",
                MinWords = 150,
                PrepSeconds = 20,
                ResponseSeconds = 60,
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            db.ExamItems.Add(new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = modality,
                DisplayOrder = i + 1,
            });
            await db.SaveChangesAsync();
        }

        var role = new Role { Code = $"r{Guid.NewGuid():N}"[..12], Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var adminRole = new Role { Code = $"a{Guid.NewGuid():N}"[..12], Name = "Admin" };
        db.Roles.Add(adminRole);
        await db.SaveChangesAsync();

        var admin = new User
        {
            RoleId = adminRole.RoleId,
            Email = $"{Guid.NewGuid():N}@poolpractice.admin",
            PasswordHash = [1],
            FullName = "Pool practice admin",
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@poolpractice.test",
            PasswordHash = [1],
            FullName = "Pool practice learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // The credit gate is not what these tests are about; give the learner enough to pass it so the
        // failure under test is the content lookup, not the balance.
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = user.UserId,
            Reason = "admin_grant",
            Delta = 10,
            AdminUserId = admin.UserId,
            Note = "Pool practice fixture",
        });
        await db.SaveChangesAsync();

        return new Fixture(user.UserId, cert.CertId, exam.ExamId);
    }
}

