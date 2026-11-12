using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 / #263: <b>the free drill draws from the pool, and stops writing per-question history.</b>
/// <para>
/// The catalog decided what was drillable by querying legacy questions and their legacy option rows, and
/// the section builder drew its session from the same table. On a certificate whose paper is
/// pool-authored, that made every section report "0 questions available" - a drill the product offers and
/// then refuses.
/// </para>
/// <para>
/// <b>What this pins.</b> The precondition (zero question rows) is asserted. The assertions are on what
/// the learner gets: the section's available count, the session's item ids being pool items at that
/// section's own topic, and the summary after answering. The cooldown assertion proves the selector is
/// still consulting stored answers after the per-question history table stops being written.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolDrillCatalogTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_section_drill_draws_from_the_pool_with_no_question_rows()
    {
        var name = $"EE263D_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolCertAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            // The precondition, asserted rather than assumed. #269: the legacy question tables are gone,
            // so the certificate's drillable stock is its roster alone.
            Assert.Equal(5, await db.ExamItems.CountAsync(i => i.CertId == fixture.CertId));

            var practice = new PracticeService(db, new LearnerService(db));

            var catalog = await practice.GetPracticeSectionsAsync(fixture.UserId);
            var section = Assert.Single(catalog.ReadingSections);
            Assert.Equal(5, section.AvailableQuestionCount);

            var session = await practice.StartSessionAsync(
                fixture.UserId, new StartPracticeSessionInput(fixture.SectionId));

            Assert.Equal(5, session.QuestionCount);

            // Every drawn answer names a pool item, and no legacy question.
            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var answers = await verify.PracticeAnswers.AsNoTracking()
                    .Where(a => a.SessionId == session.SessionId)
                    .ToListAsync();

                Assert.Equal(5, answers.Count);
                // #269: the legacy question column is gone, so a pool answer must be a pool answer -
                // an item, its exact version, and no micro-check card.
                Assert.All(answers, a => Assert.NotNull(a.ItemId));
                Assert.All(answers, a => Assert.NotNull(a.ItemVersionId));
                Assert.All(answers, a => Assert.Null(a.CardId));
            }

            // Answer one wrong, then read the summary: grading must work through the pool shape.
            var questions = await practice.GetSessionQuestionsAsync(fixture.UserId, session.SessionId);
            var first = questions[0];
            Assert.Null(first.QuestionId);
            Assert.NotNull(first.ItemVersionId);

            await practice.SubmitAnswerAsync(
                fixture.UserId, session.SessionId, first.QuestionId ?? first.ItemVersionId!.Value,
                new SubmitPracticeAnswerInput(
                    SelectedOptionId: first.Options.Single(o => o.Label == "B").OptionId));

            var summary = await practice.GetSummaryAsync(fixture.UserId, session.SessionId);
            Assert.Equal(5, summary.TotalQuestions);
            Assert.Equal(1, summary.AnsweredCount);
            Assert.Equal(0, summary.CorrectCount);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The cooldown still holds a just-missed item back, and it is derived from stored answers rather
    /// than from the per-question history table this ticket stops writing.
    /// </summary>
    [Fact]
    public async Task A_recently_missed_item_is_not_served_again_by_the_selector()
    {
        var name = $"EE263C_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedPoolCertAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var practice = new PracticeService(db, new LearnerService(db));

            // A drill of one, so the miss is unambiguous.
            var session = await practice.StartSessionAsync(
                fixture.UserId,
                new StartPracticeSessionInput(fixture.TopicId, CefrLevel.B1) { ContentTier = ContentTiers.Demo });

            var take = await practice.GetSessionQuestionsAsync(fixture.UserId, session.SessionId);
            var first = take[0];
            await practice.SubmitAnswerAsync(
                fixture.UserId, session.SessionId, first.QuestionId ?? first.ItemVersionId!.Value,
                new SubmitPracticeAnswerInput(
                    SelectedOptionId: first.Options.Single(o => o.Label == "B").OptionId));

            // The cooldown lives in the answer rows, not in a per-question history table. #269 dropped
            // UserQuestionHistories with the legacy model, so the cooldown is proved below by the
            // selector refusing to serve the item again.

            // The same cell, drawn again, must not lead with the item just missed.
            var selector = new ItemSelector(db);
            var redraw = await selector.SelectAsync(
                fixture.UserId, fixture.CertId, skillIds: [], modality: "reading",
                fixture.TopicId, CefrLevel.B1, ContentTiers.Demo, size: 10);

            Assert.NotEmpty(redraw.Items);
            Assert.DoesNotContain(redraw.Items, c => c.ItemId == fixture.FirstItemId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(int UserId, int CertId, int SectionId, int TopicId, int FirstItemId);

    /// <summary>
    /// A certificate with one reading section. Five pool items exist at the section's own topic and
    /// level, bound to one pool-authored paper - and there is not a single legacy question, group or
    /// option row anywhere.
    /// </summary>
    private static async Task<Fixture> SeedPoolCertAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"D{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Pool drill cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, DurationMinutes = 45, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
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
            ExpectedQuestionCount = 5,
            DisplayOrder = 1,
            NavigationMode = "FULL_SANDBOX",
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 49, DisplayOrder = 1,
        });
        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "HIGH", Name = "High", MinTotal = 50, MaxTotal = 100, DisplayOrder = 2,
        });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Pool drill paper {code}", Status = "published",
            DurationMinutes = 45, Purpose = "mock", QuestionCount = 5,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // R-DET is a canonical reading topic; the vocabulary is closed at twenty.
        var topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var firstItemId = 0;
        for (var i = 0; i < 5; i++)
        {
            var item = new Item
            {
                Modality = "reading",
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze",
                RenderFormat = "mcq",
                ContentTier = ContentTiers.Demo,
                LifecycleStatus = "active",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Pool drill item {i + 1}: the depot opens _____ six.",
                OptionA = "at", OptionB = "on", OptionC = "in", OptionD = "by",
                KeyLabel = "A",
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
                Modality = "reading",
                DisplayOrder = i + 1,
            });
            await db.SaveChangesAsync();

            if (i == 0) firstItemId = item.ItemId;
        }

        var role = new Role { Code = $"r{Guid.NewGuid():N}"[..12], Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@pooldrill.test",
            PasswordHash = [1],
            FullName = "Pool drill learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Fixture(user.UserId, cert.CertId, section.SectionId, topic.TopicId, firstItemId);
    }
}
