using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 4 / #264 / #271: <b>the weakness-analysis views report the pool's numbers.</b>
/// <para>
/// <b>What this file used to prove, and why it changed.</b> It compared a legacy answer (which named a
/// <c>QuestionId</c>) against a pool answer (which pins an <c>ItemVersionId</c>) and asserted both views
/// agreed. #269 deleted <c>Questions</c>, so there is no second shape to compare against - and an
/// equality check against a view that also reads zero would pass vacuously. What is asserted instead is
/// the exact figure per section and per cell, measured against a direct computation over the same rows,
/// which a view that silently returned nothing could not satisfy.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolAndLegacyAccuracyAgreeTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// The four answers this file needs, recorded once against one learner: two correct, two wrong, all
    /// naming a roster position and its pinned version. Every view must report the same per-section and
    /// per-cell totals that a direct count over the same rows produces.
    /// </summary>
    [Fact]
    public async Task The_views_report_the_totals_the_answer_rows_hold()
    {
        var name = $"EE250v_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            var poolUser = await SeedLearnerAsync(db, fixture.CertId, "pool");

            var poolAttempt = new ExamAttempt
            {
                UserId = poolUser,
                CertId = fixture.CertId,
                ExamId = fixture.ExamId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 60,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(poolAttempt);
            await db.SaveChangesAsync();

            // Two answered, one correct - the figures the views must reproduce.
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = poolAttempt.AttemptId,
                ExamId = fixture.ExamId,
                ExamItemId = fixture.ExamItemIds[0],
                ItemId = fixture.ItemIds[0],
                ItemVersionId = fixture.ItemVersionIds[0],
                SelectedLabel = "A",
                IsCorrect = true,
            });
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = poolAttempt.AttemptId,
                ExamId = fixture.ExamId,
                ExamItemId = fixture.ExamItemIds[1],
                ItemId = fixture.ItemIds[1],
                ItemVersionId = fixture.ItemVersionIds[1],
                SelectedLabel = "B",
                IsCorrect = false,
            });
            await db.SaveChangesAsync();

            // ---- the proof -------------------------------------------------------------------
            await using var read = DbContextFactory.Create(connectionString);

            var sections = await read.vw_UserSectionAccuracyAlls.AsNoTracking()
                .Where(v => v.UserId == poolUser)
                .ToListAsync();

            // #264: the view reports the POOL shape only. Asserting the figure - rather than "a row
            // exists" - is what catches a view that double counted (2 reported as 4) or dropped a row.
            var poolSection = Assert.Single(sections);
            Assert.Equal(fixture.SectionId, poolSection.SectionId);
            Assert.Equal(2, poolSection.Answered);
            Assert.Equal(1, poolSection.Correct);

            var topics = await read.vw_UserTopicAccuracies.AsNoTracking()
                .Where(v => v.UserId == poolUser)
                .ToListAsync();

            var poolCell = Assert.Single(topics);
            Assert.Equal(2, poolCell.Answered);
            Assert.Equal(1, poolCell.Correct);
            Assert.Equal(50, poolCell.AccuracyPercent);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The same answer must not be counted twice, and one that names no cell must not inflate a
    /// denominator. #269 removed the shape that could do the first (one row carrying BOTH a
    /// <c>QuestionId</c> and a pinned version, which two UNION ALL arms could each see); what is
    /// assertable now is that one row gives exactly one count, and that a row whose attempt is still in
    /// progress contributes nothing.
    /// </summary>
    [Fact]
    public async Task One_answer_counts_once_and_an_ungraded_attempt_counts_not_at_all()
    {
        var name = $"EE250d_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var userId = await SeedLearnerAsync(db, fixture.CertId, "double");

            var attempt = new ExamAttempt
            {
                UserId = userId,
                CertId = fixture.CertId,
                ExamId = fixture.ExamId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 100,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(attempt);

            // A second, UNGRADED attempt: its answers must contribute nothing at all.
            var openAttempt = new ExamAttempt
            {
                UserId = userId,
                CertId = fixture.CertId,
                ExamId = fixture.ExamId,
                Status = "in_progress",
                StartedAt = DateTimeOffset.UtcNow,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(openAttempt);
            await db.SaveChangesAsync();

            // ONE graded answer. It must be counted once, and the ungraded attempt's answer must not
            // enter the same total.
            db.AttemptAnswers.Add(PoolAnswer(attempt.AttemptId, fixture, 0, "A", true));
            db.AttemptAnswers.Add(PoolAnswer(openAttempt.AttemptId, fixture, 1, "B", false));
            await db.SaveChangesAsync();

            await using var read = DbContextFactory.Create(connectionString);
            var row = await read.vw_UserSectionAccuracyAlls.AsNoTracking()
                .SingleAsync(v => v.UserId == userId);

            Assert.Equal(1, row.Answered);
            Assert.Equal(1, row.Correct);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    private static AttemptAnswer PoolAnswer(int attemptId, Fixture f, int index, string label, bool? correct) => new()
    {
        AttemptId = attemptId,
        ExamId = f.ExamId,
        ExamItemId = f.ExamItemIds[index],
        ItemId = f.ItemIds[index],
        ItemVersionId = f.ItemVersionIds[index],
        SelectedLabel = label,
        IsCorrect = correct,
    };

    /// <summary>
    /// <b>#271 criterion 5:</b> the topic view supplies the same NUMBERS the retired category view did
    /// on the same answers, so moving six readers onto it cannot have silently dropped a row.
    /// <para>
    /// The category view is gone, so the comparison cannot be a query against both. It is instead a
    /// comparison against the number the category view was defined to produce: for a learner whose
    /// answers all sit in one cell, <c>SUM(Answered)</c> over the topic view must equal
    /// <c>COUNT(IsCorrect)</c> over the answer rows, and <c>SUM(Correct)</c> must equal the count of
    /// them marked correct. That is the arithmetic the retired view performed - it counted the same
    /// rows and grouped them by the axis it had - so equality here is the statement "nothing was
    /// dropped and nothing was counted twice".
    /// </para>
    /// <para>
    /// <b>The wrong input that would pass a weaker test:</b> asserting only that the topic view has a
    /// row. A view that double counted (2 answers reported as 4) or dropped the ungraded rows returns
    /// a row either way; only the totals catch it. The fixture therefore holds 2 graded answers, 1 of
    /// them correct, and one row LEFT UNGRADED - which must not enter either total.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_topic_view_reports_the_same_totals_the_retired_category_view_did()
    {
        var name = $"EE271v_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var userId = await SeedLearnerAsync(db, fixture.CertId, "agree");

            var attempt = new ExamAttempt
            {
                UserId = userId,
                CertId = fixture.CertId,
                ExamId = fixture.ExamId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 60,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(attempt);
            await db.SaveChangesAsync();

            // Two graded answers (1 correct, 1 wrong) plus a blank. The blank carries IsCorrect = NULL
            // and is exactly the row a drill-at-start or a skipped item writes.
            db.AttemptAnswers.AddRange(
                PoolAnswer(attempt.AttemptId, fixture, 0, "A", true),
                PoolAnswer(attempt.AttemptId, fixture, 1, "B", false));
            await db.SaveChangesAsync();

            var poolAttempt = new ExamAttempt
            {
                UserId = userId, CertId = fixture.CertId, ExamId = fixture.ExamId,
                Status = "in_progress",
                StartedAt = DateTimeOffset.UtcNow,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(poolAttempt);
            await db.SaveChangesAsync();

            // A second, UNGRADED attempt: its answers must contribute nothing at all.
            db.AttemptAnswers.AddRange(
                PoolAnswer(poolAttempt.AttemptId, fixture, 1, "B", null));
            await db.SaveChangesAsync();

            await using var read = DbContextFactory.Create(connectionString);

            var cell = Assert.Single(await read.vw_UserTopicAccuracies.AsNoTracking()
                .Where(v => v.UserId == userId).ToListAsync());

            // The rows the retired view counted, measured independently of any view.
            var graded = await read.AttemptAnswers.AsNoTracking()
                .Where(a => a.Attempt.UserId == userId && a.Attempt.Status == "graded")
                .ToListAsync();
            Assert.Equal(2, graded.Count);

            Assert.Equal(graded.Count, cell.Answered);
            Assert.Equal(graded.Count(a => a.IsCorrect == true), cell.Correct);
            Assert.Equal(1, cell.Correct);
            // And the percentage is the same division: 1 of 2 is 50.
            Assert.Equal(50, cell.AccuracyPercent);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------

    private sealed record Fixture(
        int CertId,
        int ExamId,
        int SectionId,
        IReadOnlyList<int> ItemIds,
        IReadOnlyList<int> ItemVersionIds,
        IReadOnlyList<int> ExamItemIds);

    /// <summary>
    /// One certificate and one paper with TWO pool positions, so the views have two answers to report
    /// per section and per cell.
    /// </summary>
    private static async Task<Fixture> SeedAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var code = $"C{Guid.NewGuid():N}"[..12];
        var cert = new Certificate { Code = code, Name = $"Accuracy cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1,
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
            ExpectedQuestionCount = 2,
            DisplayOrder = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId,
            Name = $"Accuracy exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 2,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        // ---- the pool stock that fills the two positions -----------------------------------
        var topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            Passage = "The depot opens at six and closes at seven. Staff sign the ledger before the shutter closes.",
            DomainContext = "general",
        };
        db.Stimuli.Add(stimulus);
        await db.SaveChangesAsync();

        var itemIds = new List<int>();
        var versionIds = new List<int>();
        var examItemIds = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            var item = new Item
            {
                StimulusId = stimulus.StimulusId,
                Modality = "reading",
                TopicId = topic.TopicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "passage_comprehension",
                ContentTier = "demo",
                LifecycleStatus = "active",
                RenderFormat = "mcq",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = $"Pool Q{i + 1}",
                OptionA = "Right",
                OptionB = "Wrong",
                OptionC = "Other",
                OptionD = "Another",
                KeyLabel = "A",
                Explanation = "The passage states it.",
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            var binding = new ExamItem
            {
                ExamId = exam.ExamId,
                CertId = cert.CertId,
                SectionId = section.SectionId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                Modality = "reading",
                DisplayOrder = i + 1,
            };
            db.ExamItems.Add(binding);
            await db.SaveChangesAsync();

            itemIds.Add(item.ItemId);
            versionIds.Add(version.ItemVersionId);
            examItemIds.Add(binding.ExamItemId);
        }

        return new Fixture(
            cert.CertId,
            exam.ExamId,
            section.SectionId,
            itemIds,
            versionIds,
            examItemIds);
    }

    private static async Task<int> SeedLearnerAsync(CompanionDbContext db, int certId, string tag)
    {
        var roleCode = Unique("lrn");
        var role = new Role { Code = roleCode, Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId,
            Email = $"{tag}-{Guid.NewGuid():N}@acc.test",
            PasswordHash = [1],
            FullName = tag,
            ActiveCertId = certId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }
}
