using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #247.3 / #248.2: <b>no attempt is mid-sitting on a legacy exam, and the check is automated.</b>
/// <para>
/// Both tickets phrase this identically, and the phrasing is the requirement: an operation nobody can
/// run is a sentence in a document. This file drives the service that answers the question, against a
/// real database with real trigger constraints, and asserts it names the blocker and the count.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> A test asserting "Ready is true" on a clean
/// database passes on a service that always returns true - and that service would wave a cutover
/// through with a learner mid-sitting. Each case below therefore CREATES the condition first and
/// asserts both the blocker code and the number of rows it names, and the last case proves the
/// service is not simply always unhappy.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CutoverReadinessTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private CompanionDbContext Db(string connectionString) => DbContextFactory.Create(connectionString);

    /// <summary>
    /// An exam with questions but no pool roster is a legacy exam, and an attempt still open against it
    /// is the blocker. The attempt is inserted directly because the point is the state, not the path
    /// that produces it: a sitting opened through the engine would be closed by the same test.
    /// </summary>
    [Fact]
    public async Task An_open_attempt_on_an_exam_with_no_pool_roster_blocks_the_cutover()
    {
        var name = $"EE247_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certId = await SeedCertAndExamAsync(connectionString, withRoster: false);
            var userId = await SeedLearnerAsync(connectionString, certId);
            var examId = await ExamIdAsync(connectionString);

            await using (var db = Db(connectionString))
            {
                await SeedOpenAttemptAsync(db, userId, examId, certId);
            }

            await using var read = Db(connectionString);
            var readiness = await new CutoverReadinessService(read).GetAsync();

            Assert.False(readiness.Ready);
            var blocker = Assert.Single(readiness.Blockers, b => b.Code == CutoverReadinessService.MidSittingOnLegacyExam);
            Assert.Equal(1, blocker.AffectedRows);
            // The message names the action, not only the symptom.
            Assert.Contains("Finish or abandon", blocker.Message, StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A finished attempt on the same exam is history, not a blocker. #246 preserves completed attempts;
    /// blocking on them would make the cutover permanently impossible on any database with history.
    /// </summary>
    [Fact]
    public async Task A_completed_attempt_on_a_legacy_exam_does_not_block_the_cutover()
    {
        var name = $"EE247F_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certId = await SeedCertAndExamAsync(connectionString, withRoster: false);
            var userId = await SeedLearnerAsync(connectionString, certId);
            var examId = await ExamIdAsync(connectionString);

            await using (var db = Db(connectionString))
            {
                var attempt = await SeedOpenAttemptAsync(db, userId, examId, certId);
                attempt.Status = "graded";
                await db.SaveChangesAsync();
            }

            await using var read = Db(connectionString);
            var readiness = await new CutoverReadinessService(read).GetAsync();

            // The exam still has no roster, so that blocker stays - the point is the ATTEMPT one.
            Assert.DoesNotContain(readiness.Blockers, b => b.Code == CutoverReadinessService.MidSittingOnLegacyExam);
            Assert.Contains(readiness.Blockers, b => b.Code == CutoverReadinessService.LegacyExamsCarryingNoRoster);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// An exam WITH a pool roster and an open sitting on it is exactly the state a cutover may run in:
    /// the pool can rebuild the paper, so the sitting survives. Without this case a service that always
    /// reported a blocker would pass the two above.
    /// </summary>
    [Fact]
    public async Task An_open_attempt_on_a_pool_backed_exam_is_ready()
    {
        var name = $"EE247R_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var certId = await SeedCertAndExamAsync(connectionString, withRoster: true);
            var userId = await SeedLearnerAsync(connectionString, certId);
            var examId = await ExamIdAsync(connectionString);

            await using (var db = Db(connectionString))
            {
                await SeedOpenAttemptAsync(db, userId, examId, certId);
            }

            await using var read = Db(connectionString);
            var readiness = await new CutoverReadinessService(read).GetAsync();

            Assert.True(readiness.Ready, string.Join("; ", readiness.Blockers.Select(b => b.Message)));
            Assert.Empty(readiness.Blockers);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixtures ---------------------------------------------------------------------------

    /// <summary>
    /// A certificate and one exam, optionally with a pool roster.
    /// <para>
    /// #272: the legacy <c>Questions</c> rows here STAY, because they are this file's subject, not its
    /// scaffolding - <paramref name="withRoster"/> = false builds exactly the "paper the pool cannot
    /// rebuild" that <see cref="CutoverReadinessService"/> must block on. Migrating them would delete the
    /// case under test. They are deleted with the table in #269.
    /// </para>
    /// </summary>
    private static async Task<int> SeedCertAndExamAsync(string connectionString, bool withRoster)
    {
        await using var db = DbContextFactory.Create(connectionString);
        var code = Unique("C");
        var cert = new BusinessObjects.Entities.Certificate { Code = code, Name = $"Cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new BusinessObjects.Entities.Skill
        {
            CertId = cert.CertId,
            Code = "READ",
            Name = "Reading",
            Modality = "reading",
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new BusinessObjects.Entities.Section
        {
            CertId = cert.CertId,
            SkillId = skill.SkillId,
            Modality = "reading",
            Code = "P5",
            Name = "Sentences",
            ExpectedQuestionCount = 1,
            DisplayOrder = 1,
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var exam = new BusinessObjects.Entities.Exam
        {
            CertId = cert.CertId,
            Name = $"Exam {code}",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "mock",
            QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var group = new BusinessObjects.Entities.QuestionGroup
        {
            ExamId = exam.ExamId,
            SectionId = section.SectionId,
            CertId = cert.CertId,
            DisplayOrder = 1,
            UnitNumber = 1,
        };
        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync();

        var question = new BusinessObjects.Entities.Question
        {
            ExamId = exam.ExamId,
            SectionId = section.SectionId,
            SkillId = skill.SkillId,
            GroupId = group.GroupId,
            QuestionType = "mcq",
            CefrLevel = BusinessObjects.CefrLevel.B1,
            Stem = "What time does the depot open?",
            DisplayOrder = 1,
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();

        if (withRoster)
        {
            // A pool-backed exam: one topic, one item, one version, one binding. The roster is what
            // tells the service this paper survives the cutover. The topic code is one of the closed
            // twenty - trg_Topics_ClosedVocabulary refuses an invented code, which is the rule working.
            var topic = new BusinessObjects.Entities.Topic
            {
                Modality = "reading",
                Code = "R-DET",
                Name = "Local factual retrieval and paraphrase matching",
            };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();

            var stimulus = new BusinessObjects.Entities.Stimulus
            {
                CefrLevel = BusinessObjects.CefrLevel.B1,
                Passage = "The depot opens at six and closes at seven.",
                DomainContext = "general",
            };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync();

            var item = new BusinessObjects.Entities.Item
            {
                StimulusId = stimulus.StimulusId,
                Modality = "reading",
                TopicId = topic.TopicId,
                CefrLevel = BusinessObjects.CefrLevel.B1,
                ItemType = "passage_comprehension",
                ContentTier = "demo",
                LifecycleStatus = "active",
                RenderFormat = "mcq",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new BusinessObjects.Entities.ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = "What time does the depot open?",
                OptionA = "Six",
                OptionB = "Seven",
                OptionC = "Eight",
                OptionD = "Nine",
                KeyLabel = "A",
                Explanation = "The first clause states six.",
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();

            db.ExamItems.Add(new BusinessObjects.Entities.ExamItem
            {
                ExamId = exam.ExamId,
                ItemId = item.ItemId,
                ItemVersionId = version.ItemVersionId,
                SectionId = section.SectionId,
                CertId = cert.CertId,
                Modality = "reading",
                DisplayOrder = 1,
            });
            await db.SaveChangesAsync();
        }

        return cert.CertId;
    }

    private static async Task<int> SeedLearnerAsync(string connectionString, int certId)
    {
        await using var db = DbContextFactory.Create(connectionString);
        var role = new BusinessObjects.Entities.Role { Code = Unique("learner"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = $"{Guid.NewGuid():N}@cutover.test",
            PasswordHash = [1],
            FullName = "Cutover learner",
            ActiveCertId = certId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    private static async Task<int> ExamIdAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);
        return await db.Exams.Select(e => e.ExamId).SingleAsync();
    }

    private static async Task<BusinessObjects.Entities.ExamAttempt> SeedOpenAttemptAsync(
        CompanionDbContext db, int userId, int examId, int certId)
    {
        var attempt = new BusinessObjects.Entities.ExamAttempt
        {
            UserId = userId,
            ExamId = examId,
            CertId = certId,
            Status = "in_progress",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    }
}
