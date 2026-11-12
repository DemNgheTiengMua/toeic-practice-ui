using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 1: <b><c>AttemptEngine</c> builds a paper from <c>ExamItems</c> alone, with no
/// <c>Questions</c> query anywhere on the sitting path.</b>
/// <para>
/// <b>Why this is the criterion and not a nicety.</b> Every earlier attempt to drop the three legacy
/// tables failed here: the paper's content model still lived in <c>Questions</c>, so a migrated paper
/// still had to read it. This file builds a paper with ZERO <c>Questions</c> rows - not one, not a
/// placeholder - and drives the sitting path over it: create, take, answer, navigate.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "The attempt was created" passes on a paper
/// that silently fell back to the legacy branch and found nothing, because an empty paper and a
/// pool-built paper both create a row. So the assertions are on what the learner SEES: the pinned
/// version's own stem, the navigator's length, and the stored answer's shape (an <c>ExamItemId</c> and
/// no <c>QuestionId</c>). The fixture also asserts the precondition - that <c>Questions</c> is empty for
/// this exam - because without it the test would prove nothing about a pool-only paper.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolOnlyPaperTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// A paper whose content exists ONLY on the pool: the engine creates, renders, answers and
    /// navigates it without a single question row.
    /// </summary>
    [Fact]
    public async Task A_paper_with_no_question_rows_is_sat_from_the_roster()
    {
        var name = $"EE250P_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolOnlyPaperAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            // The precondition, asserted rather than assumed. #269: the three legacy tables are gone, so
            // "this paper has no question rows" is now a fact about the schema, not a row count. The
            // roster is the whole paper.
            Assert.Equal(3, await db.ExamItems.CountAsync(i => i.ExamId == fixture.ExamId));

            var engine = new AttemptEngine(db);

            var attempt = await engine.CreateAttemptAsync(fixture.UserId, fixture.CertId, fixture.ExamId, 30);
            var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, fixture.UserId, questionDisplayOrder: 1);

            Assert.Equal(3, take.TotalQuestions);
            Assert.Equal(3, take.Navigator.Count);
            Assert.StartsWith("Pool item", take.CurrentQuestion.Stem, StringComparison.Ordinal);
            Assert.Equal(4, take.CurrentQuestion.Options.Count);

            var correctOptionId = take.CurrentQuestion.Options.Single(o => o.Label == "A").OptionId;
            await engine.SaveAnswerAsync(
                attempt.AttemptId, fixture.UserId, take.CurrentQuestion.QuestionId, correctOptionId, isFlagged: false);

            // The stored answer names a roster position. #269: the legacy question column is gone, so
            // the claim is that the position the sitting handed the learner is the one recorded.
            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var answer = await verify.AttemptAnswers.AsNoTracking()
                    .SingleAsync(a => a.AttemptId == attempt.AttemptId && a.ExamItemId == fixture.FirstExamItemId);
                Assert.Equal(take.CurrentQuestion.QuestionId, answer.ExamItemId);
                Assert.Equal(fixture.FirstItemVersionId, answer.ItemVersionId);
                Assert.Equal("A", answer.SelectedLabel);
                Assert.True(answer.IsCorrect, "the pinned version's key is A");
            }

            // Navigation across the whole paper.
            for (var position = 1; position <= 3; position++)
            {
                var page = await engine.GetAttemptTakeAsync(
                    attempt.AttemptId, fixture.UserId, questionDisplayOrder: position);
                Assert.Equal(position, page.CurrentQuestion.DisplayOrder);
                Assert.StartsWith("Pool item", page.CurrentQuestion.Stem, StringComparison.Ordinal);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A pool paper's navigator counts the position that was ANSWERED, and no other. This is what the
    /// roster identity buys: a position is answered when an answer names THAT position, which a
    /// version-keyed lookup could not tell apart when one version is bound twice.
    /// </summary>
    [Fact]
    public async Task The_navigator_marks_the_position_that_was_answered_and_no_other()
    {
        var name = $"EE250N_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolOnlyPaperAsync(connectionString, reuseVersion: true);

            await using var db = DbContextFactory.Create(connectionString);
            var engine = new AttemptEngine(db);
            var attempt = await engine.CreateAttemptAsync(fixture.UserId, fixture.CertId, fixture.ExamId, 30);

            var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, fixture.UserId, questionDisplayOrder: 1);

            // Every position pins the SAME version when reuseVersion is set: answering one must not mark
            // the others, which is exactly what a version-keyed count would get wrong.
            var second = take.Questions![1];
            var option = second.Options.Single(o => o.Label == "A").OptionId;
            await engine.SaveAnswerAsync(attempt.AttemptId, fixture.UserId, second.QuestionId, option, isFlagged: false);

            var after = await engine.GetAttemptTakeAsync(attempt.AttemptId, fixture.UserId, questionDisplayOrder: 1);
            Assert.Equal(1, after.AnsweredCount);
            Assert.False(after.Navigator[0].IsAnswered);
            Assert.True(after.Navigator[1].IsAnswered);
            Assert.False(after.Navigator[2].IsAnswered);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(int UserId, int CertId, int ExamId, int FirstExamItemId, int FirstItemVersionId);

    /// <summary>
    /// A published paper whose entire content lives on the pool: section, topic, items, versions and
    /// roster bindings. Deliberately NO <c>QuestionGroups</c>, no <c>Questions</c>, no
    /// <c>QuestionOptions</c> - the tables criterion 5 drops.
    /// </summary>
    private static async Task<Fixture> SeedPoolOnlyPaperAsync(
        string connectionString, bool reuseVersion = false)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Pool-only cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, MinScaledScore = 100, MaxScaledScore = 500, ScoreStep = 50,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P5",
            Name = "Incomplete Sentences",
            OptionCount = 4,
            ExpectedQuestionCount = 3,
            DisplayOrder = 1,
            NavigationMode = "FULL_SANDBOX",
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 249, DisplayOrder = 1,
        });
        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "HIGH", Name = "High", MinTotal = 250, MaxTotal = 990, DisplayOrder = 2,
        });
        await db.SaveChangesAsync();

        db.ScoreScales.Add(new ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 0, ScaledScore = 0,
        });
        db.ScoreScales.Add(new ScoreScale
        {
            CertId = cert.CertId, SkillId = skill.SkillId, ExamId = null, RawScore = 3, ScaledScore = 300,
        });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId,
            Name = $"Pool-only exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 3,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var topic = new Topic
        {
            Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching",
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var firstExamItemId = 0;
        var firstVersionId = 0;
        var firstItemId = 0;

        for (var i = 0; i < 3; i++)
        {
            int itemId;
            int versionId;

            if (reuseVersion && i > 0)
            {
                // Every position pins the SAME item and version: "the same item twice on one paper gets
                // two positions". A version-keyed answer lookup cannot distinguish these.
                itemId = firstItemId;
                versionId = firstVersionId;
            }
            else
            {
                var item = new Item
                {
                    Modality = "reading",
                    TopicId = topic.TopicId,
                    CefrLevel = CefrLevel.B1,
                    ItemType = "sentence_cloze",
                    RenderFormat = "mcq",
                    ContentTier = "demo",
                    LifecycleStatus = "active",
                };
                db.Items.Add(item);
                await db.SaveChangesAsync();

                var version = new ItemVersion
                {
                    ItemId = item.ItemId,
                    VersionNumber = 1,
                    Stem = $"Pool item {i + 1}: the depot opens _____ six.",
                    OptionA = "at", OptionB = "on", OptionC = "in", OptionD = "by",
                    KeyLabel = "A",
                    Explanation = "A clock time takes 'at'.",
                };
                db.ItemVersions.Add(version);
                await db.SaveChangesAsync();

                itemId = item.ItemId;
                versionId = version.ItemVersionId;

                if (i == 0)
                {
                    firstItemId = itemId;
                    firstVersionId = versionId;
                }
            }

            var roster = new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = itemId,
                ItemVersionId = versionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            };
            db.ExamItems.Add(roster);
            await db.SaveChangesAsync();

            if (i == 0)
            {
                firstExamItemId = roster.ExamItemId;
            }
        }

        var role = new Role { Code = Unique("lrn"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@poolonly.test",
            PasswordHash = [1],
            FullName = "Pool-only learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Fixture(user.UserId, cert.CertId, exam.ExamId, firstExamItemId, firstVersionId);
    }
}
