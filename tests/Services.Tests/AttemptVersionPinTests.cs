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
/// #186: an attempt answers a version, and a past attempt renders the version it sat.
/// <para>
/// The legacy <c>Questions</c>/<c>QuestionOptions</c> rows in this fixture deliberately disagree with
/// the pinned version — different stem, different option text, different key — so no test below can
/// pass by reading the old path. #231 deletes those rows; this ticket has to be honest before they go.
/// </para>
/// <para>
/// The schema half (the pair cannot be half-filled, the version must belong to the item, the label is
/// A–D) is proved in T-SQL by <c>docs/schema-tests/domain24-attempt-versions.sql</c>.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AttemptVersionPinTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AttemptEngine CreateEngine() => new(DbContextFactory.Create(database.ConnectionString));

    private sealed record Paper(
        int CertId,
        int ExamId,
        int ReadingSkillId,
        int SectionId,
        int LegacyQuestionId,
        int PinnedVersionId,
        int SecondLegacyQuestionId,
        int SecondPinnedVersionId);

    /// <summary>
    /// One reading section holding two positions, each with a legacy question row and a bound pool
    /// version. Both halves exist at once because that is the expand state #186 lands in.
    /// </summary>
    private async Task<Paper> SeedBoundPaperAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Pinned Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 60, 100, 450, 50);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1), (@cert, 'HIGH', N'High', 500, 990, 2);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @skill, NULL, 0, 100), (@cert, @skill, NULL, 1, 450), (@cert, @skill, NULL, 2, 900);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Pinned paper', 'published', 60, 'mock', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            -- the legacy shape, with its own stems, option text and key (B), none of which is the version's
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Legacy stem one.', 'A2', 'mcq', 1),
                   (@grp, @exam, @sec, @skill, N'Legacy stem two.', 'A2', 'mcq', 2);
            DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 1);
            DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 2);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'legacy-one-a', 0), (@q1, 'mcq', 'B', N'legacy-one-b', 1),
              (@q1, 'mcq', 'C', N'legacy-one-c', 0), (@q1, 'mcq', 'D', N'legacy-one-d', 0),
              (@q2, 'mcq', 'A', N'legacy-two-a', 1), (@q2, 'mcq', 'B', N'legacy-two-b', 0),
              (@q2, 'mcq', 'C', N'legacy-two-c', 0), (@q2, 'mcq', 'D', N'legacy-two-d', 0);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval and paraphrase matching');
            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', '{certCode}'),
                   ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', '{certCode}');
            DECLARE @item1 INT = (SELECT MIN(ItemId) FROM Items WHERE BatchTag = '{certCode}');
            DECLARE @item2 INT = (SELECT MAX(ItemId) FROM Items WHERE BatchTag = '{certCode}');

            -- the pinned key is C, and the legacy row's key is B: only one read path can score it right
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item1, 1, N'Pinned stem one.',
                    N'pinned-one-a', N'pinned-one-b', N'pinned-one-c', N'pinned-one-d', 'C',
                    N'The pinned version one explanation.'),
                   (@item2, 1, N'Pinned stem two.',
                    N'pinned-two-a', N'pinned-two-b', N'pinned-two-c', N'pinned-two-d', 'A',
                    N'The pinned version two explanation.');
            DECLARE @v1 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item1);
            DECLARE @v2 INT = (SELECT ItemVersionId FROM ItemVersions WHERE ItemId = @item2);

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
              (@exam, @sec, @cert, @v1, @item1, 'reading', 1),
              (@exam, @sec, @cert, @v2, @item2, 'reading', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions
            .Where(q => q.ExamId == exam.ExamId)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync();
        var bindings = await db.ExamItems
            .Where(b => b.ExamId == exam.ExamId)
            .OrderBy(b => b.DisplayOrder)
            .ToListAsync();

        return new Paper(
            cert.CertId,
            exam.ExamId,
            cert.CertId == 0 ? 0 : await db.Skills.Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync(),
            await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync(),
            questions[0].QuestionId,
            bindings[0].ItemVersionId,
            questions[1].QuestionId,
            bindings[1].ItemVersionId);
    }

    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Pinned Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private static int PinnedOptionId(int itemVersionId, char label) => itemVersionId * 10 + (label - 'A') + 1;

    /// <summary>
    /// AC1, first half: the sitting projection reads the bound version. The legacy stem and option text
    /// are different strings, so a projection still reading <c>Questions</c> cannot produce these.
    /// </summary>
    [Fact]
    public async Task The_sitting_projection_renders_the_bound_version_not_the_legacy_question_row()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);

        var question = take.CurrentQuestion;
        Assert.Equal(paper.LegacyQuestionId, question.QuestionId);
        Assert.Equal("Pinned stem one.", question.Stem);
        Assert.Equal(
            new[] { "pinned-one-a", "pinned-one-b", "pinned-one-c", "pinned-one-d" },
            question.Options.Select(o => o.Text));
        Assert.Equal(new[] { "A", "B", "C", "D" }, question.Options.Select(o => o.Label));

        var second = take.Questions!.Single(q => q.QuestionId == paper.SecondLegacyQuestionId);
        Assert.Equal("Pinned stem two.", second.Stem);
        Assert.DoesNotContain("legacy", second.Stem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC1, second half: the answer row records which version was answered, and the version it records
    /// is the one the paper bound — not the item's latest, which does not exist yet.
    /// </summary>
    [Fact]
    public async Task The_answer_row_records_the_version_the_binding_pinned()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.AttemptId)
            .OrderBy(a => a.QuestionId)
            .ToListAsync();

        Assert.Equal(2, answers.Count);
        Assert.All(answers, a =>
        {
            Assert.NotNull(a.ItemId);
            Assert.NotNull(a.ItemVersionId);
        });
        Assert.Equal(paper.PinnedVersionId, answers.Single(a => a.QuestionId == paper.LegacyQuestionId).ItemVersionId);
        Assert.Equal(paper.SecondPinnedVersionId, answers.Single(a => a.QuestionId == paper.SecondLegacyQuestionId).ItemVersionId);
    }

    /// <summary>
    /// AC1/AC2: answering records the label on the pinned version and scores it against that version's
    /// key. The legacy row says B is correct and the version says C, so <c>IsCorrect</c> here is only
    /// true if the pinned key decided it.
    /// </summary>
    [Fact]
    public async Task Saving_an_answer_records_the_pinned_label_and_scores_against_the_pinned_key()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        await engine.SaveAnswerAsync(
            attempt.AttemptId, userId, paper.LegacyQuestionId, PinnedOptionId(paper.PinnedVersionId, 'C'), isFlagged: false);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answer = await db.AttemptAnswers.SingleAsync(a =>
            a.AttemptId == attempt.AttemptId && a.QuestionId == paper.LegacyQuestionId);

        Assert.Equal(paper.PinnedVersionId, answer.ItemVersionId);
        Assert.Equal("C", answer.SelectedLabel);
        Assert.Null(answer.SelectedOptionId);
        Assert.True(answer.IsCorrect, "the pinned version's key is C; the legacy row's key is B and must not decide this");

        // the sitting projection reads the answer back through the same decode it wrote with
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
        Assert.Equal(PinnedOptionId(paper.PinnedVersionId, 'C'), take.CurrentQuestion.SelectedOptionId);
    }

    /// <summary>
    /// A pinned option id that names no option of the pinned version is refused, and the legacy
    /// question's own option ids are not a way in: they do not decode against the version.
    /// </summary>
    [Fact]
    public async Task Saving_an_option_that_is_not_a_label_of_the_pinned_version_is_refused()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var legacyOptionId = await db.QuestionOptions
            .Where(o => o.QuestionId == paper.LegacyQuestionId && o.IsCorrect)
            .Select(o => o.OptionId)
            .SingleAsync();

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, paper.LegacyQuestionId, legacyOptionId, isFlagged: false));

        Assert.Equal(AttemptEngineException.InvalidOption, ex.Code);
    }

    /// <summary>
    /// AC2, the criterion that has to be observed rather than asserted: a past attempt renders the
    /// version the learner sat. The attempt is created, answered and graded; only then is version 2 of
    /// that item written — with a different stem, different options and a different key. The review
    /// must come back as version 1 in every one of those fields.
    /// </summary>
    [Fact]
    public async Task A_past_attempt_renders_the_version_it_sat_after_the_item_is_re_versioned()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        await engine.SaveAnswerAsync(
            attempt.AttemptId, userId, paper.LegacyQuestionId, PinnedOptionId(paper.PinnedVersionId, 'C'), isFlagged: false);
        var submitted = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
        Assert.Equal(AttemptStatus.Graded.ToCode(), submitted.Attempt.Status);

        // A correction after the sitting. Copy-on-write means a new row, and the answer keeps version 1.
        await database.SqlAsync($"""
            DECLARE @item INT = (SELECT ItemId FROM ItemVersions WHERE ItemVersionId = {paper.PinnedVersionId});
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 2, N'Corrected stem one.',
                    N'corrected-one-a', N'corrected-one-b', N'corrected-one-c', N'corrected-one-d', 'A',
                    N'The corrected explanation.');
            UPDATE Items SET LifecycleStatus = 'retired' WHERE ItemId = @item;
            """);

        var review = await CreateEngine().GetAttemptReviewAsync(attempt.AttemptId, userId);
        var question = review.Questions.Single(q => q.QuestionId == paper.LegacyQuestionId);

        Assert.Equal("Pinned stem one.", question.Stem);
        Assert.Equal(
            new[] { "pinned-one-a", "pinned-one-b", "pinned-one-c", "pinned-one-d" },
            question.Options.Select(o => o.Text));
        Assert.Equal("The pinned version one explanation.", question.Explanation);

        // the answer still points at C of version 1, judged by version 1's key
        var selected = question.Options.Single(o => o.OptionId == question.SelectedOptionId);
        Assert.Equal("C", selected.Label);
        Assert.True(selected.IsCorrect);
        Assert.True(question.IsCorrect);
        Assert.Equal(
            new[] { "C" },
            question.Options.Where(o => o.IsCorrect).Select(o => o.Label));
    }

    /// <summary>
    /// AC3: an attempt whose binding points at a position with no question behind it is refused, and
    /// refused before anything is written. The schema already makes <c>ItemVersionId</c> NOT NULL and
    /// foreign-keyed, so a binding cannot literally miss its version; the reachable form of "the
    /// binding resolves to nothing" is a bound position the paper has no question for — a section
    /// edited after assembly. Nothing is created: no attempt, no answer.
    /// </summary>
    [Fact]
    public async Task An_attempt_is_refused_before_any_write_when_a_binding_has_no_question_behind_it()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Position 3 of the section: inside the declared envelope, past the last question.
        await database.SqlAsync($"""
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
            SELECT ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, 3
            FROM ExamItems
            WHERE ExamId = {paper.ExamId} AND DisplayOrder = 1;
            IF @@ROWCOUNT <> 1
              THROW 50000, 'fixture: the binding at position 3 was not written, so this test would prove nothing', 1;
            """);

        await using var setupDb = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(3, await setupDb.ExamItems.CountAsync(b => b.ExamId == paper.ExamId));

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            CreateEngine().CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60));

        Assert.Equal(AttemptEngineException.PaperNotAssembled, ex.Code);
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(0, await db.ExamAttempts.CountAsync(a => a.UserId == userId && a.ExamId == paper.ExamId));
        Assert.Equal(0, await db.AttemptAnswers.CountAsync(a => a.Attempt.UserId == userId));
    }

    /// <summary>
    /// The review counterpart of the test above: an answer recorded before the paper was assembled
    /// has no pin, so it renders from the question row it was answered from — even once the paper gains
    /// bindings. Rendering today's binding would show options the learner was never offered and lose
    /// the one they picked.
    /// </summary>
    [Fact]
    public async Task A_review_of_an_answer_with_no_pin_keeps_the_question_it_was_answered_from()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Answer the legacy paper first: no bindings, so no pin.
        await database.SqlAsync($"DELETE FROM ExamItems WHERE ExamId = {paper.ExamId};");

        var engine = CreateEngine();
        var legacyAttempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        await using (var legacyDb = DbContextFactory.Create(database.ConnectionString))
        {
            var legacyOptionId = await legacyDb.QuestionOptions
                .Where(o => o.QuestionId == paper.LegacyQuestionId && o.IsCorrect)
                .Select(o => o.OptionId)
                .SingleAsync();
            await engine.SaveAnswerAsync(legacyAttempt.AttemptId, userId, paper.LegacyQuestionId, legacyOptionId, isFlagged: false);
        }
        await engine.SubmitAttemptAsync(legacyAttempt.AttemptId, userId, isAutoSubmit: false);

        // Now the paper is assembled. The finished attempt must not move.
        await database.SqlAsync($"""
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = {paper.CertId});
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
            SELECT {paper.ExamId}, @sec, {paper.CertId}, v.ItemVersionId, v.ItemId, i.Modality,
                   ROW_NUMBER() OVER (ORDER BY v.ItemVersionId)
            FROM ItemVersions v
            JOIN Items i ON i.ItemId = v.ItemId
            WHERE i.BatchTag = '{certCode}';
            IF @@ROWCOUNT <> 2
              THROW 50000, 'fixture: the paper was not re-assembled, so this test would prove nothing', 1;
            """);

        var review = await CreateEngine().GetAttemptReviewAsync(legacyAttempt.AttemptId, userId);
        var question = review.Questions.Single(q => q.QuestionId == paper.LegacyQuestionId);

        Assert.Equal("Legacy stem one.", question.Stem);
        Assert.Equal(
            new[] { "legacy-one-a", "legacy-one-b", "legacy-one-c", "legacy-one-d" },
            question.Options.Select(o => o.Text));
        // the B option the legacy key marks correct is still the selected one, so the review shows it
        var selected = question.Options.Single(o => o.OptionId == question.SelectedOptionId);
        Assert.Equal("B", selected.Label);
        Assert.True(question.IsCorrect);
    }

    /// <summary>
    /// A paper that binds nothing still renders exactly as it did before #186, with no pool content and
    /// no error. This is the state of every paper #231 has not reached, so it must keep working until
    /// those rows go.
    /// </summary>
    [Fact]
    public async Task An_unbound_paper_still_renders_its_legacy_question_rows()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);

        // Remove the bindings: this is now a paper the pool has not assembled, the pre-#185 shape.
        await database.SqlAsync($"DELETE FROM ExamItems WHERE ExamId = {paper.ExamId};");
        await database.SqlAsync($"""
            UPDATE Exams SET Status = 'published' WHERE ExamId = {paper.ExamId};
            """);

        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);

        Assert.Equal("Legacy stem one.", take.CurrentQuestion.Stem);
        Assert.Equal(
            new[] { "legacy-one-a", "legacy-one-b", "legacy-one-c", "legacy-one-d" },
            take.CurrentQuestion.Options.Select(o => o.Text));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answer = await db.AttemptAnswers.SingleAsync(a =>
            a.AttemptId == attempt.AttemptId && a.QuestionId == paper.LegacyQuestionId);
        Assert.Null(answer.ItemId);
        Assert.Null(answer.ItemVersionId);
    }
}
