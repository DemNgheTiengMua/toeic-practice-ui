using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 / #261: <b>a productive task on a pool-authored paper can be sat.</b>
/// <para>
/// <b>Why this needs its own test.</b> The free-response row was keyed by the legacy question and the
/// column was NOT NULL, so a paper whose productive task lives only on the roster could not be created
/// at all - the engine refused it by name rather than writing a fake id. The schema has since been
/// relaxed (<c>FreeResponses.QuestionId</c> nullable, <c>ExamItemId</c> added, <c>CK_FreeResp_HasShape</c>
/// requiring at least one), so what remained was the write itself.
/// </para>
/// <para>
/// <b>What this pins that a create-only test would miss.</b> Four readers key a free-response row by the
/// legacy column: the draft save, the answer save, the resume sheet and the sitting's answered-count.
/// A dictionary keyed by a nullable column throws on the SECOND pool row, and a lookup that compares
/// against the question id finds nothing on the first. So this drives create, draft, take, resume and
/// submit in one sitting, and asserts the row's shape at the end: an <c>ExamItemId</c>, no
/// <c>QuestionId</c>.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolProductiveTaskTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_pool_paper_with_a_writing_task_can_be_sat_from_start_to_submit()
    {
        var name = $"EE261W_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolWritingPaperAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            // The precondition, asserted rather than assumed: the paper's content is the roster only.
            // #269: the legacy question tables are gone, so this is the roster count alone.
            Assert.Equal(2, await db.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId));

            var engine = new AttemptEngine(db);

            var attempt = await engine.CreateAttemptAsync(fixture.UserId, fixture.CertId, fixture.ExamId, 40);

            // One row per delivered productive task, each naming its roster position.
            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var responses = await verify.FreeResponses.AsNoTracking()
                    .Where(fr => fr.AttemptId == attempt.AttemptId)
                    .ToListAsync();
                Assert.Equal(2, responses.Count);
                Assert.All(responses, r => Assert.Equal("free_response", r.QuestionType));
                Assert.Contains(responses, r => r.ExamItemId == fixture.FirstExamItemId);
                Assert.Contains(responses, r => r.ExamItemId == fixture.SecondExamItemId);
            }

            var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, fixture.UserId, questionDisplayOrder: 1);
            Assert.Equal(2, take.TotalQuestions);
            Assert.Equal(0, take.AnsweredCount);

            // A draft against the position the paper gave the learner.
            var draft = await engine.SaveDraftAsync(
                attempt.AttemptId, fixture.UserId, take.CurrentQuestion.QuestionId, "A short draft answer.");
            Assert.Equal(take.CurrentQuestion.QuestionId, draft.QuestionId);

            // Resume must read the draft back, keyed by the same position.
            var resume = await engine.ResumeAttemptByCodeAsync(attempt.AuthorizationCode!, fixture.UserId);
            Assert.Equal("A short draft answer.", resume.AnswerSheet[0].ResponseText);

            var afterDraft = await engine.GetAttemptTakeAsync(
                attempt.AttemptId, fixture.UserId, questionDisplayOrder: 1);
            Assert.Equal(1, afterDraft.AnsweredCount);
            Assert.True(afterDraft.Navigator[0].IsAnswered);

            // Submit settles the untouched second task and queues the written one.
            var graded = await engine.SubmitAttemptAsync(attempt.AttemptId, fixture.UserId, isAutoSubmit: false);

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var responses = await verify.FreeResponses.AsNoTracking()
                    .Where(fr => fr.AttemptId == attempt.AttemptId)
                    .OrderBy(fr => fr.FreeResponseId)
                    .ToListAsync();

                // One row per delivered task, both addressed by the roster position.
                Assert.Equal(2, responses.Count);
                Assert.Equal(
                    new[] { fixture.FirstExamItemId, fixture.SecondExamItemId }.OrderBy(id => id),
                    responses.Select(r => r.ExamItemId).OrderBy(id => id));
            }

            Assert.Equal(AttemptStatus.Submitted.ToCode(), graded.Attempt.Status);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(int UserId, int CertId, int ExamId, int FirstExamItemId, int SecondExamItemId);

    /// <summary>
    /// A published writing paper whose two tasks exist only on the pool: no question group, no question
    /// row, no option row. Two tasks rather than one, because the second is what a nullable-keyed
    /// dictionary cannot survive.
    /// </summary>
    private static async Task<Fixture> SeedPoolWritingPaperAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"W{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Pool writing cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "WRIT", Name = "Writing", Modality = "writing",
            DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 9, ScoreStep = 1,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "writing",
            Code = "W1",
            Name = "Writing Tasks",
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
            Name = $"Pool writing exam {code}",
            Status = "published",
            DurationMinutes = 40,
            Purpose = "mock",
            QuestionCount = 2,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // W-ARG is the canonical writing code for "persuasive essay": the topic vocabulary is a closed
        // set of twenty and an invented code is refused by trg_Topics_ClosedVocabulary.
        var topic = new Topic
        {
            Modality = "writing", Code = "W-ARG", Name = "Persuasive essay",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var rosterIds = new List<int>(2);
        for (var i = 0; i < 2; i++)
        {
            var item = new Item
            {
                Modality = "writing",
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B2,
                ItemType = "persuasive_essay",
                RenderFormat = "free_response",
                ContentTier = "demo",
                LifecycleStatus = "active",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            // A productive version: no options, no key, a one-sided word floor.
            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Pool writing task {i + 1}: argue for or against the proposal.",
                MinWords = 150,
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            var roster = new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = "writing",
                DisplayOrder = i + 1,
            };
            db.ExamItems.Add(roster);
            await db.SaveChangesAsync();

            rosterIds.Add(roster.ExamItemId);
        }

        var role = new Role { Code = $"w{Guid.NewGuid():N}"[..12], Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@poolwriting.test",
            PasswordHash = [1],
            FullName = "Pool writing learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Fixture(user.UserId, cert.CertId, exam.ExamId, rosterIds[0], rosterIds[1]);
    }
}
