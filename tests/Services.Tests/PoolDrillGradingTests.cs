using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #272: a pool drill must record a verdict for every question it can judge, and must render every
/// question in the control its content asks for.
/// <para>
/// <b>Why this file exists.</b> Three shapes were measured recording NO verdict, or the wrong one, and
/// none of them were visible to a green suite because the fixtures still authored the legacy tables:
/// </para>
/// <list type="bullet">
/// <item><c>multi_select</c> keyed <c>A,B</c> answered with both labels stored <c>IsCorrect = null</c>,
/// because <c>PracticeService</c> graded one label against a key that names two.</item>
/// <item><c>inline_completion</c> answered <c>"  DEVELOPMENT  "</c> against accepted
/// <c>["development","software development"]</c> stored <c>IsCorrect = null</c>, because a blank has no
/// options and the service read "no options" as "no verdict" - while the EXAM engine graded the very
/// same version, so a drill and a sitting disagreed about one item.</item>
/// <item>The same blank was projected to the take screen as <c>free_response</c> with
/// <c>AcceptedAnswersJson = null</c> and <c>MaxWords = null</c>, so the client drew a long-answer box
/// for a one-word completion.</item>
/// </list>
/// <para>
/// <b>What a wrong implementation could still pass.</b> "IsCorrect is not null" passes for a drill that
/// marks everything correct. Each test therefore asserts the exact verdict on BOTH a right and a wrong
/// answer, so a grader that always answers the same way fails whichever way it answers.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolDrillGradingTests(DatabaseFixture database)
{
    private static CompanionDbContext Db(string connectionString) => DbContextFactory.Create(connectionString);

    /// <summary>
    /// A one-question free drill over a single item version. The item is authored directly rather than
    /// through the authoring services, because the subject is grading, not authoring.
    /// </summary>
    private static async Task SeedDrillAsync(string connectionString, ItemVersion version, string modality = "reading")
    {
        await using var db = Db(connectionString);

        // A pool drill names a cell (topic + level), not a section: CK_PracticeSessions_Shape refuses a
        // session that claims both. The certificate is still needed to scope the learner.
        var cert = new Certificate
        {
            Code = $"EE272_{Guid.NewGuid():N}"[..18],
            Name = "Pool drill grading",
            Description = null,
            IsActive = true,
        };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var topic = await db.Topics.Where(t => t.Modality == modality).OrderBy(t => t.TopicId).FirstOrDefaultAsync()
            ?? await AddTopicAsync(db, modality);

        var item = new Item
        {
            Modality = modality,
            TopicId = topic.TopicId,
            CefrLevel = CefrLevel.B1,
            // CK_Items_Modality_Type closes the vocabulary per modality, and CK_Items_Modality_RenderFormat
            // refuses an objective render in a productive skill.
            ItemType = modality switch
            {
                "writing" => "transactional_correspondence",
                "speaking" => "descriptive_monologue",
                "listening" => "short_exchange",
                _ => "sentence_cloze",
            },
            RenderFormat = version.OptionA is null
                ? (modality is "writing" or "speaking" ? "free_response" : "inline_completion")
                : (version.KeyLabel is not null && version.KeyLabel.Contains(',') ? "multi_select" : "mcq"),
            PlacementEligible = true,
            ContentTier = ContentTiers.Demo,
            LifecycleStatus = "active",
            BatchTag = $"pg{Guid.NewGuid():N}"[..12],
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        version.ItemId = item.ItemId;
        version.VersionNumber = 1;
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        // The role vocabulary is lowercase in the seed ('learner', 'admin'), and a schema-only database
        // may carry none of it, so provision it the way PoolFixture provisions a Topic.
        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).FirstOrDefaultAsync();
        if (roleId == 0)
        {
            var role = new Role { Code = "learner", Name = "Learner" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            roleId = role.RoleId;
        }

        // Answering a certificate starts with the learner's active certificate.
        var user = new User
        {
            Email = $"pg{Guid.NewGuid():N}@demo.local",
            PasswordHash = [],
            FullName = "Drill learner",
            RoleId = roleId,
            ActiveCertId = cert.CertId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            TimeZoneId = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // CK_PracticeSessions_Shape: a session is one shape or the other. A POOL drill carries the cell
        // (TopicId + CefrLevel) and no section, which is exactly the shape this ticket is about.
        // QuestionCount 2 with one answer row: the session must survive the first submission, so the
        // three-arm test can submit a right answer and a wrong one against the same question shape.
        var session = new PracticeSession
        {
            UserId = user.UserId,
            CertId = cert.CertId,
            TopicId = topic.TopicId,
            CefrLevel = CefrLevel.B1,
            Modality = modality,
            QuestionSource = "all",
            Status = PracticeStatus.InProgress.ToCode(),
            StartedAt = DateTimeOffset.UtcNow,
            QuestionCount = 2,
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            DisplayOrder = 1,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A fresh learner and a fresh one-question session over the newest seeded item version. Each arm of
    /// a test needs its own session: a second submission to the same answer row is refused by design
    /// (<c>AlreadyAnswered</c>), and that refusal is a different rule from the one under test here.
    /// </summary>
    private static async Task<(int UserId, int SessionId, int ItemVersionId)> NewDrillAsync(
        string connectionString)
    {
        await using var db = Db(connectionString);

        var version = await db.ItemVersions.OrderByDescending(v => v.ItemVersionId).FirstAsync();
        var item = await db.Items.SingleAsync(i => i.ItemId == version.ItemId);
        var cert = await db.Certificates.OrderByDescending(c => c.CertId).FirstAsync();
        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).FirstAsync();

        var user = new User
        {
            Email = $"pg{Guid.NewGuid():N}@demo.local",
            PasswordHash = [],
            FullName = "Drill learner",
            RoleId = roleId,
            ActiveCertId = cert.CertId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            TimeZoneId = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var session = new PracticeSession
        {
            UserId = user.UserId,
            CertId = cert.CertId,
            TopicId = item.TopicId,
            CefrLevel = item.CefrLevel,
            Modality = item.Modality,
            QuestionSource = "all",
            Status = PracticeStatus.InProgress.ToCode(),
            StartedAt = DateTimeOffset.UtcNow,
            QuestionCount = 2,
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            DisplayOrder = 1,
        });
        await db.SaveChangesAsync();

        return (user.UserId, session.SessionId, version.ItemVersionId);
    }

    [Fact]
    public async Task A_multi_select_is_right_only_when_the_whole_label_set_is_chosen()
    {
        var name = $"EE272ms_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);

        await SeedDrillAsync(connectionString, new ItemVersion
        {
            Stem = "Which two statements are supported by the passage?",
            OptionA = "The policy was reviewed.",
            OptionB = "Overtime fell.",
            OptionC = "Sales doubled.",
            OptionD = "The pilot was cancelled.",
            KeyLabel = "A,B",
            MaxSelections = 2,
        });

        int rightA, rightB, wrongD;
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var service = new PracticeService(db, new LearnerService(db));

            var take = await service.GetSessionTakeAsync(drill.UserId, drill.SessionId);
            var question = Assert.Single(take.Questions);

            // The render shape must be multi_select - a single-choice control would discard the second
            // choice before grading ever ran.
            Assert.Equal(QuestionType.MultiSelect.ToCode(), question.QuestionType);
            Assert.Equal(2, question.MaxSelections);
            Assert.Equal(4, question.Options.Count);

            rightA = question.Options.Single(o => o.Label == "A").OptionId;
            rightB = question.Options.Single(o => o.Label == "B").OptionId;
            wrongD = question.Options.Single(o => o.Label == "D").OptionId;
        }

        // Both correct labels: right.
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
                drill.UserId, drill.SessionId, drill.ItemVersionId,
                new SubmitPracticeAnswerInput { SelectedOptionIds = [rightA, rightB] });

            Assert.True(result.IsCorrect, "both keyed labels were chosen, so the answer is right");
        }

        // One label short: wrong. This is the assertion a "records something" test cannot make.
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
                drill.UserId, drill.SessionId, drill.ItemVersionId,
                new SubmitPracticeAnswerInput { SelectedOptionIds = [rightA] });

            Assert.False(result.IsCorrect, "a multi_select keyed A,B is not satisfied by A alone");
        }

        // An extra label the key does not name: also wrong. A set comparison is the only rule that
        // refuses this, which is why the two-label key is what the fixture declares.
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
                drill.UserId, drill.SessionId, drill.ItemVersionId,
                new SubmitPracticeAnswerInput { SelectedOptionIds = [rightA, rightB, wrongD] });

            Assert.False(result.IsCorrect, "choosing a label the key does not name makes the answer wrong");
        }
    }

    [Fact]
    public async Task A_text_completion_is_judged_by_its_accepted_answers_like_the_exam_engine_does()
    {
        var name = $"EE272ic_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);

        await SeedDrillAsync(connectionString, new ItemVersion
        {
            Stem = "The team is currently ____ a new scheduling tool.",
            AcceptedAnswersJson = "[\"development\",\"software development\"]",
            AllowNumber = true,
        });

        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);

            var take = await new PracticeService(db, new LearnerService(db)).GetSessionTakeAsync(drill.UserId, drill.SessionId);
            var question = Assert.Single(take.Questions);

            // The control the learner is given must be a completion box, not a long-answer box.
            Assert.Equal(QuestionType.InlineCompletion.ToCode(), question.QuestionType);
            Assert.Equal("[\"development\",\"software development\"]", question.AcceptedAnswersJson);
        }

        // A right answer, with the whitespace and case a learner actually types: right.
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
                drill.UserId, drill.SessionId, drill.ItemVersionId,
                new SubmitPracticeAnswerInput { ResponseText = "  DEVELOPMENT  " });

            Assert.True(result.IsCorrect, "the accepted answer is \"development\" and the learner typed it");
        }

        // A wrong answer: wrong. Both arms, so a grader stuck at one verdict fails.
        {
            var drill = await NewDrillAsync(connectionString);
            await using var db = Db(connectionString);
            var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
                drill.UserId, drill.SessionId, drill.ItemVersionId,
                new SubmitPracticeAnswerInput { ResponseText = "deployment" });

            Assert.False(result.IsCorrect, "\"deployment\" is not an accepted answer");
        }
    }

    /// <summary>
    /// A productive task carries no key and no accepted answers, so it stays UNGRADED — the behaviour
    /// that must survive this fix, because marking a writing drill's response correct made the session
    /// summary report 100% accuracy for work no one had judged.
    /// </summary>
    [Fact]
    public async Task A_productive_task_stays_ungraded()
    {
        var name = $"EE272pr_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);

        await SeedDrillAsync(connectionString, new ItemVersion
        {
            Stem = "Write a sentence describing the chart.",
            MinWords = 8,
            MaxWords = 40,
            ResponseSeconds = 600,
        }, modality: "writing");

        var drill = await NewDrillAsync(connectionString);
        await using var db = Db(connectionString);

        var result = await new PracticeService(db, new LearnerService(db)).SubmitAnswerAsync(
            drill.UserId, drill.SessionId, drill.ItemVersionId,
            new SubmitPracticeAnswerInput { ResponseText = "Revenue rose sharply across all four quarters." });

        Assert.Null(result.IsCorrect);
        Assert.Equal("Revenue rose sharply across all four quarters.", result.ResponseText);
    }

    /// <summary>The session's own row, resolved by whichever identity it carries.</summary>
    private static async Task<(int UserId, int SessionId, int ItemVersionId)> DrillAsync(CompanionDbContext db)
    {
        var session = await db.PracticeSessions.OrderBy(s => s.SessionId).FirstAsync();
        var answer = await db.PracticeAnswers.FirstAsync(a => a.SessionId == session.SessionId);
        return (session.UserId, session.SessionId, answer.ItemVersionId!.Value);
    }

    /// <summary>
    /// A topic for the modality, provisioned when the fresh database has none - the same self-provision
    /// <c>PoolFixture</c> does, so this file does not depend on a seed being loaded.
    /// </summary>
    private static async Task<Topic> AddTopicAsync(CompanionDbContext db, string modality)
    {
        var topic = new Topic
        {
            Modality = modality,
            Code = modality switch
            {
                "listening" => "L-DET",
                "speaking" => "S-DESC",
                "writing" => "W-CORR",
                _ => "R-DET",
            },
            Name = modality switch
            {
                "listening" => "Listening for detail",
                "speaking" => "Describing",
                "writing" => "Transactional correspondence",
                _ => "Reading for detail",
            },
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic;
    }
}
