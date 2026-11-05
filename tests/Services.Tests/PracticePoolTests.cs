using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #187: a drill reads through the pool.
/// <para>
/// The fixture authors two active pool items with no exam anywhere, so no test below can pass by
/// reading the legacy <c>Questions</c> path — there is no question row to read. The one test that
/// needs the legacy shape builds it explicitly and says so.
/// </para>
/// <para>
/// The schema half (a session is one shape or the other, a pool answer pins a version, a duplicate is
/// refused) is proved in T-SQL by <c>docs/schema-tests/domain25-practice-pool.sql</c>.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PracticePoolTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private sealed record Cell(
        int CertId,
        int TopicId,
        CefrLevel Level,
        int FirstItemId,
        int FirstVersionId,
        int SecondItemId,
        int SecondVersionId);

    private PracticeService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), new LearnerService(DbContextFactory.Create(database.ConnectionString)));

    /// <summary>
    /// One certificate (so a learner has an active one), one reading topic, and two active B1 pool
    /// items with a version each. Nothing here names an exam: that is the whole point of AC1.
    /// <para>
    /// The topic vocabulary is a closed set of twenty, so the pool's cells are shared with every other
    /// fixture in the suite. Each test in this class therefore owns the cell for the length of its run
    /// by retiring only the items this class authored (<see cref="BatchPrefix"/>) — a fixture that
    /// retired a neighbour's stock would be a defect in the suite, not a pool rule.
    /// </para>
    /// </summary>
    private const string BatchPrefix = "practicetest";

    private async Task<Cell> SeedCellAsync(string certCode, string topicCode = "R-INF")
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Pool Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = '{topicCode}')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', '{topicCode}', N'Bridging inference and logical deduction');
            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = '{topicCode}');

            UPDATE Items SET LifecycleStatus = 'retired'
            WHERE TopicId = @topic AND CefrLevel = 'B1' AND Modality = 'reading'
              AND LifecycleStatus = 'active' AND BatchTag LIKE '{BatchPrefix}%';

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag) VALUES
              ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', '{BatchPrefix}-{certCode}-a'),
              ('reading', @topic, 'B1', 'sentence_cloze', 'production', 'active', '{BatchPrefix}-{certCode}-b');
            DECLARE @item1 INT = (SELECT ItemId FROM Items WHERE BatchTag = '{BatchPrefix}-{certCode}-a');
            DECLARE @item2 INT = (SELECT ItemId FROM Items WHERE BatchTag = '{BatchPrefix}-{certCode}-b');

            -- the two keys differ, so a test that mixes them up cannot pass by accident
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation) VALUES
              (@item1, 1, N'Pool stem one.', N'one-a', N'one-b', N'one-c', N'one-d', 'C', N'Pool explanation one.'),
              (@item2, 1, N'Pool stem two.', N'two-a', N'two-b', N'two-c', N'two-d', 'A', N'Pool explanation two.');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var topic = await db.Topics.SingleAsync(t => t.Modality == "reading" && t.Code == topicCode);
        var versions = await db.ItemVersions
            .Where(v => v.Item.BatchTag == $"{BatchPrefix}-{certCode}-a" || v.Item.BatchTag == $"{BatchPrefix}-{certCode}-b")
            .OrderBy(v => v.ItemId)
            .ToListAsync();

        return new Cell(
            cert.CertId,
            topic.TopicId,
            CefrLevel.B1,
            versions[0].ItemId,
            versions[0].ItemVersionId,
            versions[1].ItemId,
            versions[1].ItemVersionId);
    }

    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Pool Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>The option id #186's bijection derives for a label of a version.</summary>
    private static int OptionId(int itemVersionId, char label) => itemVersionId * 10 + (label - 'A') + 1;

    /// <summary>
    /// AC1: a session starts against pool items with no exam behind them. The assertions below are
    /// the whole shape of a pool drill: a cell, no section, and answers that name items and versions
    /// rather than questions.
    /// </summary>
    [Fact]
    public async Task A_session_starts_against_pool_items_with_no_exam_behind_them()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));

        Assert.Null(session.SectionId);
        Assert.Equal(cell.TopicId, session.TopicId);
        Assert.Equal(cell.Level, session.CefrLevel);
        Assert.Equal(Modality.Reading, session.Modality);
        Assert.Equal(2, session.QuestionCount);
        Assert.Equal(PracticeStatus.InProgress, session.Status);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
        Assert.Null(row.SectionId);
        Assert.Equal(cell.CertId, row.CertId);

        var answers = await db.PracticeAnswers.AsNoTracking()
            .Where(a => a.SessionId == session.SessionId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync();

        Assert.Equal(2, answers.Count);
        Assert.All(answers, a =>
        {
            Assert.Null(a.QuestionId);
            Assert.Null(a.SectionId);
            Assert.NotNull(a.ItemId);
            Assert.NotNull(a.ItemVersionId);
        });
        Assert.Equal(
            new[] { cell.FirstVersionId, cell.SecondVersionId },
            answers.Select(a => a.ItemVersionId!.Value));

        // and nothing anywhere named an exam
        Assert.Equal(0, await db.ExamItems.CountAsync(b => b.ItemId == cell.FirstItemId || b.ItemId == cell.SecondItemId));
    }

    /// <summary>
    /// The sitting renders the pool item: the stem, the four options with derived ids, and the key
    /// only after an answer. There is no question row for this item, so a projection still reading
    /// <c>Questions</c> cannot produce the stem at all.
    /// </summary>
    [Fact]
    public async Task The_sitting_renders_the_pool_item_and_reveals_the_key_only_after_an_answer()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));
        var take = await practice.GetSessionTakeAsync(userId, session.SessionId);

        var question = take.Question;
        Assert.Null(question.QuestionId);
        Assert.Equal(cell.FirstVersionId, question.ItemVersionId);
        Assert.Equal("Pool stem one.", question.Stem);
        Assert.Equal(new[] { "one-a", "one-b", "one-c", "one-d" }, question.Options.Select(o => o.Text));
        Assert.Equal(
            new[] { OptionId(cell.FirstVersionId, 'A'), OptionId(cell.FirstVersionId, 'B'), OptionId(cell.FirstVersionId, 'C'), OptionId(cell.FirstVersionId, 'D') },
            question.Options.Select(o => o.OptionId));
        Assert.Null(question.CorrectOptionId);
        Assert.Null(question.Explanation);
    }

    /// <summary>
    /// AC2: the answer pins the version and is graded by that version's key.
    /// </summary>
    [Fact]
    public async Task Saving_a_pool_answer_pins_the_version_and_scores_against_its_key()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));
        var result = await practice.SubmitAnswerAsync(
            userId, session.SessionId, cell.FirstVersionId, new SubmitPracticeAnswerInput(OptionId(cell.FirstVersionId, 'C')));

        Assert.True(result.IsCorrect);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answer = await db.PracticeAnswers.SingleAsync(a =>
            a.SessionId == session.SessionId && a.ItemVersionId == cell.FirstVersionId);

        Assert.Equal(cell.FirstItemId, answer.ItemId);
        Assert.Equal("C", answer.SelectedLabel);
        Assert.Null(answer.SelectedOptionId);
        Assert.Null(answer.QuestionId);
    }

    /// <summary>
    /// A version's own option ids are the only way in. The ids of another version do not decode
    /// against this one, so they are refused rather than silently graded against nothing.
    /// </summary>
    [Fact]
    public async Task An_option_id_that_names_no_option_of_the_pinned_version_is_refused()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(
                userId, session.SessionId, cell.FirstVersionId, new SubmitPracticeAnswerInput(OptionId(cell.SecondVersionId, 'A'))));

        Assert.Equal(PracticeException.InvalidOption, ex.Code);
    }

    /// <summary>
    /// AC2, the criterion that has to be observed rather than asserted: a re-versioned item does not
    /// alter a completed session's summary. The session is created, answered and finished; only then
    /// is version 2 of that item written — with a different stem, different options and a different
    /// key. The summary must come back as version 1 in every field.
    /// </summary>
    [Fact]
    public async Task A_re_versioned_item_does_not_alter_a_completed_session_summary()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));

        // answer both items so the session finishes on the last answer, not on a re-read
        await practice.SubmitAnswerAsync(
            userId, session.SessionId, cell.FirstVersionId, new SubmitPracticeAnswerInput(OptionId(cell.FirstVersionId, 'C')));
        await practice.SubmitAnswerAsync(
            userId, session.SessionId, cell.SecondVersionId, new SubmitPracticeAnswerInput(OptionId(cell.SecondVersionId, 'A')));

        var before = await practice.GetSummaryAsync(userId, session.SessionId);
        Assert.Equal(PracticeStatus.Finished, (await practice.GetSessionInfoAsync(userId, session.SessionId)).Status);

        // a correction after the sitting. Copy-on-write means a new row, and the answers keep version 1.
        await database.SqlAsync($"""
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES ({cell.FirstItemId}, 2, N'Corrected stem one.',
                    N'corrected-one-a', N'corrected-one-b', N'corrected-one-c', N'corrected-one-d', 'A',
                    N'The corrected explanation.');
            UPDATE Items SET LifecycleStatus = 'retired' WHERE ItemId = {cell.FirstItemId};
            """);

        var after = await CreateService().GetSummaryAsync(userId, session.SessionId);
        var question = after.Questions.Single(q => q.DisplayOrder == 1);

        Assert.Equal("Pool stem one.", question.Stem);
        Assert.Equal("one-c", question.SelectedOptionText);
        Assert.Equal("C", question.SelectedOptionLabel);
        Assert.Equal("C", question.CorrectOptionLabel);
        Assert.Equal("one-c", question.CorrectOptionText);
        Assert.Equal("Pool explanation one.", question.Explanation);
        Assert.True(question.IsCorrect);
        Assert.DoesNotContain(after.Questions, q => q.Stem.Contains("Corrected", StringComparison.OrdinalIgnoreCase));

        // the summary's own arithmetic did not move either
        Assert.Equal(before.TotalQuestions, after.TotalQuestions);
        Assert.Equal(before.AnsweredCount, after.AnsweredCount);
        Assert.Equal(before.CorrectCount, after.CorrectCount);
        Assert.Equal(before.AccuracyPercentage, after.AccuracyPercentage);
        Assert.Equal(
            before.Questions.Select(q => (q.Stem, q.SelectedOptionLabel, q.CorrectOptionLabel)),
            after.Questions.Select(q => (q.Stem, q.SelectedOptionLabel, q.CorrectOptionLabel)));
    }

    /// <summary>
    /// AC3: existing practice behaviour still holds on the pool path. A missed item is served last in
    /// the next draw rather than repeated in this one, and a cell with fewer items than a session is
    /// short rather than padded.
    /// </summary>
    [Fact]
    public async Task A_pool_session_is_short_rather_than_padded_and_serves_a_missed_item_last()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var first = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));
        Assert.Equal(2, first.QuestionCount); // the whole cell, honestly short

        await practice.SubmitAnswerAsync(
            userId, first.SessionId, cell.FirstVersionId, new SubmitPracticeAnswerInput(OptionId(cell.FirstVersionId, 'A')));

        // A session started straight away draws what the cell holds. The missed item is still in the
        // pool - a pool drill is not blocked by a cooldown - but it goes last, never first.
        var second = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));
        var secondQuestions = await practice.GetSessionQuestionsAsync(userId, second.SessionId);

        Assert.Equal(2, secondQuestions.Count);
        Assert.Equal(cell.SecondVersionId, secondQuestions[0].ItemVersionId);
        Assert.Equal(cell.FirstVersionId, secondQuestions[1].ItemVersionId);
    }

    /// <summary>
    /// A pool drill takes its certificate from the learner's active certificate, so switching
    /// certificate cannot mix two pools.
    /// </summary>
    [Fact]
    public async Task A_pool_session_carries_the_learners_active_certificate()
    {
        var certCode = Unique("POOL");
        var otherCode = Unique("OTHR");
        var cell = await SeedCellAsync(certCode);
        await database.SqlAsync($"INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{otherCode}', N'Other', 1);");
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var row = await db.PracticeSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
        Assert.Equal(cell.CertId, row.CertId);
        Assert.NotEqual(
            await db.Certificates.Where(c => c.Code == otherCode).Select(c => c.CertId).SingleAsync(),
            row.CertId);
    }

    /// <summary>
    /// A cell with no active items is refused by name rather than started as an empty session.
    /// A retired item is not stock.
    /// </summary>
    [Fact]
    public async Task A_cell_with_no_active_items_refuses_the_session()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'retired' WHERE BatchTag IN ('{BatchPrefix}-{certCode}-a', '{BatchPrefix}-{certCode}-b');
            """);

        var ex = await Assert.ThrowsAsync<PracticeException>(() =>
            practice.StartSessionAsync(userId, new StartPracticeSessionInput(cell.TopicId, cell.Level)));

        Assert.Equal(PracticeException.NoQuestionsAvailable, ex.Code);
    }

    /// <summary>
    /// A drill is one shape or the other. Asking for both is refused before any query runs.
    /// </summary>
    [Fact]
    public async Task Starting_a_drill_that_names_both_a_section_and_a_cell_is_refused()
    {
        var certCode = Unique("POOL");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var practice = CreateService();

        var input = new StartPracticeSessionInput(1)
        {
            TopicId = cell.TopicId,
            CefrLevel = cell.Level,
        };

        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            practice.StartSessionAsync(userId, input));
    }
}
