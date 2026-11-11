using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 4: <b>the two weakness-analysis views return the same numbers before and after the
/// migration, proved by a query against both shapes on the same data.</b>
/// <para>
/// <b>Why this file exists.</b> Both views already carry a pool branch, written when the pool arrived
/// beside the paper. It has never run: measured on the seeded database, <c>AttemptAnswers</c> holds 16
/// rows and ZERO of them carry an <c>ItemVersionId</c>. A branch that has never executed is not known to
/// work - it is known to compile. This drives the same answers through both shapes and asserts the
/// aggregates agree, which is what makes the pool branch safe to rely on when #250 deletes the legacy
/// tables.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "The pool branch returns something" passes on a
/// branch that double-counts: <c>vw_UserSectionAccuracyAll</c> has three UNION ALL arms and a pool answer
/// that also kept a <c>QuestionId</c> is visible to two of them. Its legacy arm consequently excludes
/// pinned rows. So the assertion is EQUALITY of the totals between the two shapes, not the presence of a
/// row - a doubled count fails it, and so does a dropped one.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolAndLegacyAccuracyAgreeTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// The SAME four answers, recorded twice against two learners: once as legacy rows that name a
    /// QuestionId, once as pool rows that pin an ItemVersionId. Every view must report the same
    /// per-section and per-topic totals for the two learners.
    /// </summary>
    [Fact]
    public async Task The_views_report_the_same_totals_for_a_pool_answer_and_a_legacy_answer()
    {
        var name = $"EE250v_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var fixture = await SeedAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);

            // ---- the two learners ------------------------------------------------------------
            var legacyUser = await SeedLearnerAsync(db, fixture.CertId, "legacy");
            var poolUser = await SeedLearnerAsync(db, fixture.CertId, "pool");

            // ---- legacy shape: answers name a QuestionId -------------------------------------
            var legacyAttempt = new ExamAttempt
            {
                UserId = legacyUser,
                CertId = fixture.CertId,
                ExamId = fixture.ExamId,
                Status = "graded",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                TotalScore = 60,
                WrittenByService = DerivedFactWriters.AttemptScoreAndBand,
                ProjectionVersion = 1,
            };
            db.ExamAttempts.Add(legacyAttempt);
            await db.SaveChangesAsync();

            // Two answered, one correct - the same figures the pool learner gets.
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = legacyAttempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = fixture.QuestionIds[0],
                SelectedOptionId = fixture.CorrectOptionIds[0],
                IsCorrect = true,
            });
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = legacyAttempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = fixture.QuestionIds[1],
                SelectedOptionId = fixture.WrongOptionIds[1],
                IsCorrect = false,
            });
            await db.SaveChangesAsync();

            // ---- pool shape: answers pin an ItemVersionId ------------------------------------
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

            // A pool answer keeps a QuestionId: AttemptAnswers.QuestionId is NOT NULL until #250
            // removes the column, which is exactly why the views must state which shape owns a row.
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = poolAttempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = fixture.QuestionIds[0],
                ItemId = fixture.ItemIds[0],
                ItemVersionId = fixture.ItemVersionIds[0],
                SelectedLabel = "A",
                IsCorrect = true,
            });
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = poolAttempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = fixture.QuestionIds[1],
                ItemId = fixture.ItemIds[1],
                ItemVersionId = fixture.ItemVersionIds[1],
                SelectedLabel = "B",
                IsCorrect = false,
            });
            await db.SaveChangesAsync();

            // ---- the proof -------------------------------------------------------------------
            await using var read = DbContextFactory.Create(connectionString);

            var sections = await read.vw_UserSectionAccuracyAlls.AsNoTracking()
                .Where(v => v.UserId == legacyUser || v.UserId == poolUser)
                .ToListAsync();

            var legacySection = Assert.Single(sections, s => s.UserId == legacyUser);
            var poolSection = Assert.Single(sections, s => s.UserId == poolUser);

            // Equality, not presence: a double-counted pool row would report 4 answered here.
            Assert.Equal(2, legacySection.Answered);
            Assert.Equal(1, legacySection.Correct);
            Assert.Equal(legacySection.Answered, poolSection.Answered);
            Assert.Equal(legacySection.Correct, poolSection.Correct);
            Assert.Equal(legacySection.SectionId, poolSection.SectionId);

            var topics = await read.vw_UserTopicAccuracies.AsNoTracking()
                .Where(v => v.UserId == legacyUser || v.UserId == poolUser)
                .ToListAsync();

            // The topics view reads Items, so the legacy learner has no cell at all - that is the
            // point of criterion 4's "the same numbers": the pool shape must supply the cell the
            // legacy shape cannot, and it must be counted once.
            var poolCell = Assert.Single(topics, t => t.UserId == poolUser);
            Assert.Equal(2, poolCell.Answered);
            Assert.Equal(1, poolCell.Correct);
            Assert.Equal(50, poolCell.AccuracyPercent);
            Assert.Empty(topics.Where(t => t.UserId == legacyUser));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The same answers counted twice would be the defect the legacy arm's exclusion exists to prevent.
    /// This drives the case directly: one pool answer whose QuestionId is ALSO resolvable, and asserts
    /// the section figure is 1 rather than 2.
    /// </summary>
    [Fact]
    public async Task A_pool_answer_that_also_names_a_question_is_counted_once()
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
            await db.SaveChangesAsync();

            // ONE answer. It carries both shapes: a QuestionId (which the legacy arm joins on) and a
            // pinned ItemVersionId (which the pool arm joins on).
            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attempt.AttemptId,
                ExamId = fixture.ExamId,
                QuestionId = fixture.QuestionIds[0],
                ItemId = fixture.ItemIds[0],
                ItemVersionId = fixture.ItemVersionIds[0],
                SelectedLabel = "A",
                IsCorrect = true,
            });
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

            AttemptAnswer PoolAnswer(int attemptId, Fixture f, int index, string label, bool? correct) => new()
            {
                AttemptId = attemptId,
                ExamId = f.ExamId,
                ExamItemId = f.ExamItemIds[index],
                ItemId = f.ItemIds[index],
                ItemVersionId = f.ItemVersionIds[index],
                SelectedLabel = label,
                IsCorrect = correct,
            };
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
        IReadOnlyList<int> QuestionIds,
        IReadOnlyList<int> CorrectOptionIds,
        IReadOnlyList<int> WrongOptionIds,
        IReadOnlyList<int> ItemIds,
        IReadOnlyList<int> ItemVersionIds,
        IReadOnlyList<int> ExamItemIds);

    /// <summary>
    /// One certificate, one paper with two questions, and TWO pool items bound to the same exam - so a
    /// legacy answer and a pool answer describe the same sitting and the views can be compared.
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

        var group = new QuestionGroup
        {
            ExamId = exam.ExamId, CertId = cert.CertId, SectionId = section.SectionId,
            DisplayOrder = 1, UnitNumber = 1,
        };
        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync();

        var q1 = new Question
        {
            GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId,
            SkillId = skill.SkillId, Stem = "Q1", CefrLevel = CefrLevel.B1, QuestionType = "mcq",
            DisplayOrder = 1,
        };
        var q2 = new Question
        {
            GroupId = group.GroupId, ExamId = exam.ExamId, SectionId = section.SectionId,
            SkillId = skill.SkillId, Stem = "Q2", CefrLevel = CefrLevel.B1, QuestionType = "mcq",
            DisplayOrder = 2,
        };
        db.Questions.AddRange(q1, q2);
        await db.SaveChangesAsync();

        var q1Correct = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true };
        var q1Wrong = new QuestionOption { QuestionId = q1.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false };
        var q2Correct = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "B", Text = "Right", IsCorrect = true };
        var q2Wrong = new QuestionOption { QuestionId = q2.QuestionId, QuestionType = "mcq", Label = "A", Text = "Wrong", IsCorrect = false };
        db.QuestionOptions.AddRange(q1Correct, q1Wrong, q2Correct, q2Wrong);
        await db.SaveChangesAsync();

        // ---- the pool stock the same two questions become ---------------------------------
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
            [q1.QuestionId, q2.QuestionId],
            [q1Correct.OptionId, q2Correct.OptionId],
            [q1Wrong.OptionId, q2Wrong.OptionId],
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
