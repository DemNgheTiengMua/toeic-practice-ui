using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #253: one answer, one status. The practice drill, the sitting summary and the review screen used to
/// disagree about the same row because "answered" and "graded" were the same test in three places, and
/// a productive pool answer is stored ungraded forever.
/// <para>
/// Every test here owns a database of its own: the fixtures author certificates, learners, sessions and
/// a learning path, and seeding those into the shared catalog has already broken this suite twice.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PracticeAnswerStateTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static CompanionDbContext Open(string connectionString) => DbContextFactory.Create(connectionString);

    /// <summary>
    /// One certificate with a reading skill and the canonical R-GRAM topic, plus a learner. Returns the
    /// ids the fixtures below address.
    /// </summary>
    private static async Task<(int CertId, int SkillId, int TopicId, int UserId)> SeedCertificateAndLearnerAsync(
        CompanionDbContext db, string certCode)
    {
        // A fresh catalogue is empty: even the role this learner hangs off has to be authored here.
        var learnerRole = await db.Roles.SingleOrDefaultAsync(r => r.Code == "learner");
        if (learnerRole is null)
        {
            learnerRole = new Role { Code = "learner", Name = "Learner" };
            db.Roles.Add(learnerRole);
            await db.SaveChangesAsync();
        }

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId,
            Code = "READ",
            Name = "Reading",
            Modality = "reading",
            DisplayOrder = 1,
            MinScaledScore = 100,
            MaxScaledScore = 450,
            ScoreStep = 50,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var topic = await db.Topics.SingleOrDefaultAsync(t => t.Modality == "reading" && t.Code == "R-GRAM");
        if (topic is null)
        {
            topic = new Topic { Modality = "reading", Code = "R-GRAM", Name = "Morphosyntactic structure" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
        }

        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).SingleAsync();
        var user = new User
        {
            RoleId = roleId,
            Email = $"{Guid.NewGuid():N}@test.local",
            PasswordHash = [0x01],
            FullName = "Answer State Learner",
            ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (cert.CertId, skill.SkillId, topic.TopicId, user.UserId);
    }

    /// <summary>An active pool item with a four-option version, keyed on <paramref name="keyLabel"/>.</summary>
    private static async Task<(int ItemId, int ItemVersionId)> SeedObjectiveItemAsync(
        CompanionDbContext db, int topicId, string batchTag, char keyLabel)
    {
        var item = new Item
        {
            Modality = "reading",
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            ContentTier = ContentTiers.Production,
            LifecycleStatus = "active",
            BatchTag = batchTag,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = $"Objective stem {batchTag}.",
            OptionA = "a",
            OptionB = "b",
            OptionC = "c",
            OptionD = "d",
            KeyLabel = keyLabel.ToString(),
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        return (item.ItemId, version.ItemVersionId);
    }

    /// <summary>
    /// An active pool item whose version is productive: all four options and the key are null, which is
    /// the shape <c>CK_ItemVersions_OptionsComplete</c> admits as "a writing task, not a choice".
    /// </summary>
    private static async Task<(int ItemId, int ItemVersionId)> SeedProductiveItemAsync(
        CompanionDbContext db, int topicId, string batchTag)
    {
        var item = new Item
        {
            Modality = "reading",
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            ContentTier = ContentTiers.Production,
            LifecycleStatus = "active",
            BatchTag = batchTag,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = $"Productive stem {batchTag}. Answer in your own words.",
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        return (item.ItemId, version.ItemVersionId);
    }

    private static async Task<int> SeedPoolSessionAsync(
        CompanionDbContext db, int certId, int topicId, int userId, int? learnerPathStepId, params (int ItemId, int ItemVersionId)[] items)
    {
        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = null,
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            Modality = "reading",
            QuestionCount = items.Length,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            LearnerPathStepId = learnerPathStepId,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        for (var i = 0; i < items.Length; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                ItemId = items[i].ItemId,
                ItemVersionId = items[i].ItemVersionId,
                DisplayOrder = i + 1,
            });
        }

        await db.SaveChangesAsync();
        return session.SessionId;
    }

    /// <summary>The option id <c>AttemptEngine</c>'s bijection derives for a label of a version.</summary>
    private static int OptionId(int itemVersionId, char label) => itemVersionId * 10 + (label - 'A') + 1;

    /// <summary>
    /// One objective position on a pool paper, in the render format the caller names.
    /// <para>
    /// #272: the three formats this file needs - <c>mcq</c>, <c>multi_select</c> and
    /// <c>inline_completion</c> - are all objective, so each is an <c>ItemVersion</c> whose columns carry
    /// what the legacy question row used to. <c>PoolFixture.AddMcqAsync</c> covers only the plain mcq, so
    /// the other two are authored here rather than widening the shared fixture for one caller.
    /// </para>
    /// </summary>
    private static async Task<PoolFixture.Position> AddPoolPositionAsync(
        CompanionDbContext db, int examId, int certId, int sectionId, int skillId,
        string renderFormat, string stem, int displayOrder)
    {
        var topicId = await db.Topics
            .Where(t => t.Modality == "reading")
            .OrderBy(t => t.TopicId)
            .Select(t => (int?)t.TopicId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("This fixture needs the reading topic seeded first.");

        var item = new Item
        {
            Modality = "reading",
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = "sentence_cloze",
            RenderFormat = renderFormat,
            ContentTier = ContentTiers.Demo,
            LifecycleStatus = "active",
            BatchTag = Unique("ans"),
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // CK_ItemVersions_OptionsComplete has two legal shapes: a keyed item with options and no accepted
        // answers, or a blank with AcceptedAnswersJson and neither options nor a key.
        var isBlank = renderFormat == "inline_completion";
        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem,
            OptionA = isBlank ? null : "right",
            OptionB = isBlank ? null : "wrong",
            OptionC = isBlank ? null : "wrong",
            OptionD = isBlank ? null : "wrong",
            KeyLabel = isBlank ? null : "A",
            AcceptedAnswersJson = isBlank ? "[\"photosynthesis\"]" : null,
            MaxSelections = renderFormat == "multi_select" ? 2 : null,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        var binding = new ExamItem
        {
            ExamId = examId,
            CertId = certId,
            SectionId = sectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = "reading",
            DisplayOrder = displayOrder,
            UnitNumber = 1,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync();

        return new PoolFixture.Position(binding.ExamItemId, item.ItemId, version.ItemVersionId);
    }

    private static PracticeService CreatePractice(CompanionDbContext db) =>
        new(db, new LearnerService(db));

    /// <summary>
    /// The defect, at its worst: a pool drill whose last answer is productive never reached Finished, so
    /// <c>FinishedAt</c> stayed null and the session was never closed.
    /// <para>
    /// Two productive items, because the old predicate is only wrong when a <b>remaining</b> row is
    /// productive: with one ungraded written answer still in the session, "has anything left to answer"
    /// was true forever.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_pool_drill_finishes_when_its_last_answer_is_a_response_with_no_option()
    {
        var dbName = $"EEAnswerFinish_{Guid.NewGuid():N}"[..25];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using var seedDb = Open(connectionString);
            var (certId, _, topicId, userId) = await SeedCertificateAndLearnerAsync(seedDb, Unique("FIN"));
            var first = await SeedProductiveItemAsync(seedDb, topicId, Unique("pro1"));
            var second = await SeedProductiveItemAsync(seedDb, topicId, Unique("pro2"));
            var sessionId = await SeedPoolSessionAsync(
                seedDb, certId, topicId, userId, learnerPathStepId: null, first, second);

            await using var db = Open(connectionString);
            var practice = CreatePractice(db);

            var one = await practice.SubmitAnswerAsync(
                userId, sessionId, first.ItemVersionId,
                new SubmitPracticeAnswerInput(ResponseText: "A written answer with no option."));
            Assert.False(one.IsSessionFinished);

            var last = await practice.SubmitAnswerAsync(
                userId, sessionId, second.ItemVersionId,
                new SubmitPracticeAnswerInput(ResponseText: "The last item, also written."));

            Assert.True(last.IsSessionFinished);
            Assert.Null(last.IsCorrect); // the writer's deliberate "not judged"; this ticket does not grade it

            var info = await practice.GetSessionInfoAsync(userId, sessionId);
            Assert.Equal(PracticeStatus.Finished, info.Status);
            Assert.NotNull(info.FinishedAt);

            // and the rows themselves are still ungraded, so nothing here turned a writing task green
            await using var verifyDb = Open(connectionString);
            var stored = await verifyDb.PracticeAnswers.AsNoTracking()
                .Where(a => a.SessionId == sessionId)
                .ToListAsync();
            Assert.All(stored, a => Assert.Null(a.IsCorrect));
            Assert.All(stored, a => Assert.False(string.IsNullOrWhiteSpace(a.ResponseText)));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The accepting control for the test above. A predicate that treats any written row as answered
    /// still has to leave a session open while an item genuinely has nothing in it — this is the input
    /// that would pass the first test while being wrong.
    /// </summary>
    [Fact]
    public async Task A_pool_drill_with_a_genuinely_unanswered_item_does_not_finish()
    {
        var dbName = $"EEAnswerOpen_{Guid.NewGuid():N}"[..25];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using var seedDb = Open(connectionString);
            var (certId, _, topicId, userId) = await SeedCertificateAndLearnerAsync(seedDb, Unique("OPN"));
            var productive = await SeedProductiveItemAsync(seedDb, topicId, Unique("pro"));
            var objective = await SeedObjectiveItemAsync(seedDb, topicId, Unique("obj"), 'B');
            var untouched = await SeedObjectiveItemAsync(seedDb, topicId, Unique("unt"), 'C');
            var sessionId = await SeedPoolSessionAsync(
                seedDb, certId, topicId, userId, learnerPathStepId: null, productive, objective, untouched);

            await using var db = Open(connectionString);
            var practice = CreatePractice(db);

            var written = await practice.SubmitAnswerAsync(
                userId, sessionId, productive.ItemVersionId,
                new SubmitPracticeAnswerInput(ResponseText: "Answered."));
            Assert.False(written.IsSessionFinished);

            var chosen = await practice.SubmitAnswerAsync(
                userId, sessionId, objective.ItemVersionId,
                new SubmitPracticeAnswerInput(OptionId(objective.ItemVersionId, 'B')));

            // the third item is still empty: two answered items are not the whole session
            Assert.False(chosen.IsSessionFinished);
            Assert.NotNull(chosen.NextQuestionDisplayOrder);

            var info = await practice.GetSessionInfoAsync(userId, sessionId);
            Assert.Equal(PracticeStatus.InProgress, info.Status);
            Assert.Null(info.FinishedAt);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// All four ways a row can carry content count as answered, and a row with none does not. Asserted
    /// through two readers (the summary and the take navigator) so one copied predicate cannot satisfy
    /// both by accident.
    /// </summary>
    [Fact]
    public async Task ResponseText_a_label_or_an_id_list_each_count_as_answered_and_an_empty_row_does_not()
    {
        var dbName = $"EEAnswerShape_{Guid.NewGuid():N}"[..25];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            int sessionId;
            await using (var seedDb = Open(connectionString))
            {
                var (certId, _, topicId, userId) = await SeedCertificateAndLearnerAsync(seedDb, Unique("SHP"));
                var items = new (int, int)[]
                {
                    await SeedProductiveItemAsync(seedDb, topicId, Unique("txt")),
                    await SeedObjectiveItemAsync(seedDb, topicId, Unique("lbl"), 'A'),
                    await SeedObjectiveItemAsync(seedDb, topicId, Unique("ids"), 'B'),
                    await SeedObjectiveItemAsync(seedDb, topicId, Unique("non"), 'C'),
                };
                sessionId = await SeedPoolSessionAsync(seedDb, certId, topicId, userId, null, items);

                var rows = await seedDb.PracticeAnswers
                    .Where(a => a.SessionId == sessionId)
                    .OrderBy(a => a.DisplayOrder)
                    .ToListAsync();

                rows[0].ResponseText = "words and no option";
                rows[0].IsCorrect = null;
                rows[1].SelectedLabel = "A";
                rows[1].IsCorrect = true;
                rows[2].SelectedOptionIdsJson = $"[{items[2].Item2 * 10 + 1}]";
                rows[2].IsCorrect = null;
                // rows[3] stays empty: no option, no label, no text, no id list
                await seedDb.SaveChangesAsync();
            }

            await using var db = Open(connectionString);
            var practice = CreatePractice(db);
            var userIdRow = await db.PracticeSessions.Where(s => s.SessionId == sessionId)
                .Select(s => s.UserId).SingleAsync();

            var take = await practice.GetSessionTakeAsync(userIdRow, sessionId);
            Assert.Equal(new[] { true, true, true, false }, take.Navigator.Select(n => n.IsAnswered));

            var summary = await practice.GetSummaryAsync(userIdRow, sessionId);
            Assert.Equal(4, summary.TotalQuestions);
            Assert.Equal(3, summary.AnsweredCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(summary.AnsweredCount, take.Navigator.Count(n => n.IsAnswered));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The consequence, not only the status flag: a drill session attached to a path step closes that
    /// step when its last answer is productive. Before this fix the block that closes the step never ran.
    /// <para>
    /// Two written items, not one, because the old predicate is only wrong while a <b>remaining</b> row
    /// is ungraded — a session mixing one objective item and one written one happened to finish.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_step_attached_to_a_pool_drill_closes_when_the_last_answer_is_productive()
    {
        var dbName = $"EEAnswerStep_{Guid.NewGuid():N}"[..25];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await using var seedDb = Open(connectionString);
            var (certId, skillId, topicId, userId) = await SeedCertificateAndLearnerAsync(seedDb, Unique("STP"));

            var path = new LearnerPath
            {
                UserId = userId,
                CertId = certId,
                Status = "active",
                StartedAt = DateTimeOffset.UtcNow,
            };
            seedDb.LearnerPaths.Add(path);
            await seedDb.SaveChangesAsync();

            var step = new LearnerPathStep
            {
                LearnerPathId = path.LearnerPathId,
                CertId = certId,
                SkillId = skillId,
                Modality = "reading",
                TopicId = topicId,
                TargetCefr = "B1",
                StepType = "focus_practice",
                SizingTier = "developing",
                Status = "in_progress",
                WeekNumber = 1,
                WeekSequence = 1,
                TargetAccuracyPct = 70,
            };
            seedDb.LearnerPathSteps.Add(step);
            await seedDb.SaveChangesAsync();

            var objective = await SeedObjectiveItemAsync(seedDb, topicId, Unique("obj"), 'A');
            var firstWritten = await SeedProductiveItemAsync(seedDb, topicId, Unique("pro1"));
            var lastWritten = await SeedProductiveItemAsync(seedDb, topicId, Unique("pro2"));
            var sessionId = await SeedPoolSessionAsync(
                seedDb, certId, topicId, userId, step.StepId, objective, firstWritten, lastWritten);

            await using var db = Open(connectionString);
            var practice = CreatePractice(db);

            await practice.SubmitAnswerAsync(
                userId, sessionId, objective.ItemVersionId,
                new SubmitPracticeAnswerInput(OptionId(objective.ItemVersionId, 'A')));
            await practice.SubmitAnswerAsync(
                userId, sessionId, firstWritten.ItemVersionId,
                new SubmitPracticeAnswerInput(ResponseText: "Written, ungraded."));
            await practice.SubmitAnswerAsync(
                userId, sessionId, lastWritten.ItemVersionId,
                new SubmitPracticeAnswerInput(ResponseText: "The last item, written."));

            await using var verifyDb = Open(connectionString);
            var closed = await verifyDb.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == step.StepId);
            Assert.Equal("done", closed.Status);
            Assert.NotNull(closed.CompletedAt);
            // one correct of three answered: the two written answers are ungraded, not wrong
            Assert.Equal(33.33m, closed.ActualAccuracyPct);

            var session = await verifyDb.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
            Assert.Equal(PracticeStatus.Finished.ToCode(), session.Status);
            Assert.NotNull(session.FinishedAt);
            Assert.Equal(1, session.CorrectCount);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The chain the issue opened with: one attempt, two screens. A text-only answer and an id-list-only
    /// answer are answered on the review, carry the correctness the grader stored, and the review's own
    /// count agrees with the sitting's. A blank third question stays unanswered and ungraded.
    /// <para>
    /// <b>BLOCKER (#272, not owned by this ticket).</b> This now fails on the assertion at
    /// <c>Assert.True(text.IsCorrect, ...)</c> for a pool paper. Cause:
    /// <c>src/Services/Attempts/AttemptEngine.Answers.cs:103</c> sets
    /// <c>answer.IsCorrect = IsKeyLabel(pinnedVersion, label)</c> and <c>label</c> is null for a text-only
    /// answer, so a pool <c>inline_completion</c> can never be judged correct - even though the version
    /// carries <c>AcceptedAnswersJson</c> and the sitting projects it
    /// (<c>AttemptEngine.Sitting.Roster.cs:154</c>). The legacy branch evaluates against the question row
    /// instead. The fixture used to mask it by authoring legacy rows; the paper is now pool-only, the shape
    /// production produces. The assertion is kept as written. Not fixed here: src/ is out of scope.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_sitting_and_the_review_agree_about_a_text_and_an_id_list_answer()
    {
        var dbName = $"EEAnswerReview_{Guid.NewGuid():N}"[..25];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            int certId;
            int attemptId;
            int learnerId;
            int multiQuestionId;
            int textQuestionId;
            int blankQuestionId;
            await using (var seedDb = Open(connectionString))
            {
                var seeded = await SeedCertificateAndLearnerAsync(seedDb, Unique("REV"));
                certId = seeded.CertId;
                learnerId = seeded.UserId;

                var section = new Section
                {
                    SkillId = seeded.SkillId,
                    CertId = certId,
                    Modality = "reading",
                    Code = "P5",
                    Name = "Incomplete Sentences",
                    OptionCount = 4,
                    ExpectedQuestionCount = 3,
                    DisplayOrder = 1,
                };
                seedDb.Sections.Add(section);
                seedDb.ScoreBands.AddRange(
                    new ScoreBand { CertId = certId, Code = "LOW", Name = "Low", MinTotal = 0, MaxTotal = 499, DisplayOrder = 1 },
                    new ScoreBand { CertId = certId, Code = "HIGH", Name = "High", MinTotal = 500, MaxTotal = 990, DisplayOrder = 2 });
                await seedDb.SaveChangesAsync();

                var exam = new Exam
                {
                    CertId = certId,
                    Name = "Answer state paper",
                    Status = "published",
                    DurationMinutes = 60,
                    Purpose = "mock",
                    QuestionCount = 3,
                };
                seedDb.Exams.Add(exam);
                await seedDb.SaveChangesAsync();

                seedDb.ScoreScales.AddRange(
                    new ScoreScale { CertId = certId, SkillId = seeded.SkillId, ExamId = exam.ExamId, RawScore = 0, ScaledScore = 100 },
                    new ScoreScale { CertId = certId, SkillId = seeded.SkillId, ExamId = exam.ExamId, RawScore = 1, ScaledScore = 450 },
                    new ScoreScale { CertId = certId, SkillId = seeded.SkillId, ExamId = exam.ExamId, RawScore = 2, ScaledScore = 450 },
                    new ScoreScale { CertId = certId, SkillId = seeded.SkillId, ExamId = exam.ExamId, RawScore = 3, ScaledScore = 450 });

                // #272: three pool positions. The render formats the test needs - multi_select,
                // inline_completion and a plain mcq - are all objective, so each is an ItemVersion whose
                // columns carry what the old question row did.
                var multi = await AddPoolPositionAsync(
                    seedDb, exam.ExamId, certId, section.SectionId, seeded.SkillId, "multi_select",
                    "Choose every correct option.", displayOrder: 1);
                var inline = await AddPoolPositionAsync(
                    seedDb, exam.ExamId, certId, section.SectionId, seeded.SkillId, "inline_completion",
                    "Complete the sentence.", displayOrder: 2);
                var blank = await AddPoolPositionAsync(
                    seedDb, exam.ExamId, certId, section.SectionId, seeded.SkillId, "mcq",
                    "Never answered.", displayOrder: 3);

                multiQuestionId = multi.ExamItemId;
                textQuestionId = inline.ExamItemId;
                blankQuestionId = blank.ExamItemId;

                var engine = new AttemptEngine(seedDb);
                var attempt = await engine.CreateAttemptAsync(learnerId, certId, exam.ExamId, 60);
                attemptId = attempt.AttemptId;

                var multiOptionId = await PoolFixture.OptionIdAsync(seedDb, exam.ExamId, multi.ExamItemId, "A");

                // text only, no option
                await engine.SaveAnswerAsync(attemptId, learnerId, textQuestionId, null, false,
                    responseText: "Photosynthesis");
                // id list only, no option id and no text
                await engine.SaveAnswerAsync(attemptId, learnerId, multiQuestionId, null, false,
                    selectedOptionIdsJson: $"[{multiOptionId}]");

                var sitting = await engine.GetAttemptTakeAsync(attemptId, learnerId, 1);
                Assert.Equal(2, sitting.AnsweredCount);
                Assert.Equal(2, sitting.Navigator.Count(n => n.IsAnswered));
                Assert.False(sitting.Navigator.Single(n => n.QuestionId == blankQuestionId).IsAnswered);

                await engine.SubmitAttemptAsync(attemptId, learnerId, isAutoSubmit: false);
            }

            await using var db = Open(connectionString);
            var review = await new AttemptEngine(db).GetAttemptReviewAsync(attemptId, learnerId);

            // the count the summary derives and the flags the review list derives are now one answer
            Assert.Equal(2, review.AnsweredCount);
            Assert.Equal(2, review.Navigator.Count(n => n.IsAnswered));
            Assert.Equal(1, review.BlankCount);

            var text = review.Questions.Single(q => q.QuestionId == textQuestionId);
            Assert.True(text.IsCorrect, "the grader scored this text-only answer right; the review used to hide it");

            var multiAnswer = review.Questions.Single(q => q.QuestionId == multiQuestionId);
            Assert.True(multiAnswer.IsCorrect);

            var blankQuestion = review.Questions.Single(q => q.QuestionId == blankQuestionId);
            Assert.Null(blankQuestion.IsCorrect);
            Assert.False(review.Navigator.Single(n => n.QuestionId == blankQuestionId).IsAnswered);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
