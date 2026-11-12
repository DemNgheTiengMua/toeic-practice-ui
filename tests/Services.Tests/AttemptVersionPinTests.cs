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
/// <b>#269: the three legacy-coexistence tests are gone.</b> They proved the seam between a paper's
/// legacy <c>Questions</c> rows and its pool bindings - a paper whose binding resolved to no question
/// being refused, an answer with no pin still rendering from its question, and an unbound paper
/// rendering its legacy rows. All three required <c>Questions</c> to exist, and the table does not, so
/// none of them describes a reachable state any more. The five pin tests below are pool-native and
/// unchanged in subject: a pin is a property of the pool paper, so they author one, and the sitting and
/// the review render it.
/// </para>
/// <para>
/// The schema half (a version must belong to the item, the label is A-D) is proved in T-SQL by
/// <c>docs/schema-tests/domain24-attempt-versions.sql</c>.
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
        int FirstPositionId,
        int FirstVersionId,
        int SecondPositionId,
        int SecondVersionId);

    /// <summary>
    /// One reading section holding two pool positions, each binding version 1 of its item. The paper is
    /// authored the way a real one is - a stimulus, an item per question, a version carrying the scored
    /// content, and a binding per position - so the pin these tests assert is the pin the pool makes.
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

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1), (@cert, 'HIGH', N'High', 500, 990, 2);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @skill, NULL, 0, 100), (@cert, @skill, NULL, 1, 450), (@cert, @skill, NULL, 2, 900);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Pinned paper', 'published', 60, 'mock', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        // The first position's key is C and the second's is A - the two disagree, so an answer that
        // names one position cannot be scored by the other position's key.
        var first = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Pinned stem one.",
            displayOrder: 1, keyLabel: "C", level: "A2",
            explanation: "The pinned version one explanation.");
        var second = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Pinned stem two.",
            displayOrder: 2, keyLabel: "A", level: "A2",
            explanation: "The pinned version two explanation.");

        return new Paper(
            cert.CertId,
            exam.ExamId,
            first.ExamItemId,
            first.ItemVersionId,
            second.ExamItemId,
            second.ItemVersionId);
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

    /// <summary>
    /// AC1, first half: the sitting projection renders the version the binding pinned. A newer version
    /// of the same item is written BEFORE the sitting, with a different stem and different option text,
    /// so a projection that read the item's latest version could not produce version 1's content.
    /// </summary>
    [Fact]
    public async Task The_sitting_projection_renders_the_bound_version_not_the_legacy_question_row()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);

        // A correction that is NOT on this paper: the binding still pins version 1.
        await database.SqlAsync($"""
            DECLARE @item INT = (SELECT ItemId FROM ItemVersions WHERE ItemVersionId = {paper.FirstVersionId});
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 2, N'Newer stem one.',
                    N'newer-one-a', N'newer-one-b', N'newer-one-c', N'newer-one-d', 'A',
                    N'The newer explanation.');
            """);

        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);

        var question = take.CurrentQuestion;
        Assert.Equal(paper.FirstPositionId, question.QuestionId);
        Assert.Equal("Pinned stem one.", question.Stem);
        // A pool MCQ is four options by construction (CNT-06); the pool's own texts are what render.
        Assert.Equal(
            new[] { "Option A", "Option B", "Option C", "Option D" },
            question.Options.Select(o => o.Text));
        Assert.Equal(new[] { "A", "B", "C", "D" }, question.Options.Select(o => o.Label));

        var second = take.Questions!.Single(q => q.QuestionId == paper.SecondPositionId);
        Assert.Equal("Pinned stem two.", second.Stem);
        Assert.DoesNotContain("newer", second.Stem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AC1, second half: the answer row records which version was answered, and the version it records
    /// is the one the paper bound - not the item's latest, which version 2 above became.
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
            .OrderBy(a => a.ExamItemId)
            .ToListAsync();

        Assert.Equal(2, answers.Count);
        Assert.All(answers, a =>
        {
            Assert.NotNull(a.ItemId);
            Assert.NotNull(a.ItemVersionId);
        });
        Assert.Equal(paper.FirstVersionId, answers.Single(a => a.ExamItemId == paper.FirstPositionId).ItemVersionId);
        Assert.Equal(paper.SecondVersionId, answers.Single(a => a.ExamItemId == paper.SecondPositionId).ItemVersionId);
    }

    /// <summary>
    /// AC1/AC2: answering records the label on the pinned version and scores it against that version's
    /// key. The two positions' keys disagree (C and A), so an answer judged by any other version's key
    /// comes back wrong.
    /// </summary>
    [Fact]
    public async Task Saving_an_answer_records_the_pinned_label_and_scores_against_the_pinned_key()
    {
        var certCode = Unique("PIN");
        var paper = await SeedBoundPaperAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, paper.CertId, paper.ExamId, 60);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var keyOptionId = await PoolFixture.OptionIdAsync(db, paper.ExamId, paper.FirstPositionId, "C");
        await engine.SaveAnswerAsync(
            attempt.AttemptId, userId, paper.FirstPositionId, keyOptionId, isFlagged: false);

        var answer = await db.AttemptAnswers.SingleAsync(a =>
            a.AttemptId == attempt.AttemptId && a.ExamItemId == paper.FirstPositionId);

        Assert.Equal(paper.FirstVersionId, answer.ItemVersionId);
        Assert.Equal("C", answer.SelectedLabel);
        Assert.Null(answer.SelectedOptionId);
        Assert.True(answer.IsCorrect, "the pinned version's key is C, and it must be the one that decides this");

        // the sitting projection reads the answer back through the same decode it wrote with
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
        Assert.Equal(keyOptionId, take.CurrentQuestion.SelectedOptionId);
    }

    /// <summary>
    /// An option id that names no option of the pinned version is refused. The id below is a real option
    /// of another position on this paper, which is what makes it a live way in: it decodes as a valid
    /// option id, just not one of THIS version's.
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
        var otherPositionsOptionId = await PoolFixture.OptionIdAsync(db, paper.ExamId, paper.SecondPositionId, "A");

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, paper.FirstPositionId, otherPositionsOptionId, isFlagged: false));

        Assert.Equal(AttemptEngineException.InvalidOption, ex.Code);
    }

    /// <summary>
    /// AC2, the criterion that has to be observed rather than asserted: a past attempt renders the
    /// version the learner sat. The attempt is created, answered and graded; only then is version 2 of
    /// that item written â€” with a different stem, different options and a different key. The review
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
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var keyOptionId = await PoolFixture.OptionIdAsync(db, paper.ExamId, paper.FirstPositionId, "C");
            await engine.SaveAnswerAsync(
                attempt.AttemptId, userId, paper.FirstPositionId, keyOptionId, isFlagged: false);
        }

        var submitted = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
        Assert.Equal(AttemptStatus.Graded.ToCode(), submitted.Attempt.Status);

        // A correction after the sitting. Copy-on-write means a new row, and the answer keeps version 1.
        await database.SqlAsync($"""
            DECLARE @item INT = (SELECT ItemId FROM ItemVersions WHERE ItemVersionId = {paper.FirstVersionId});
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 2, N'Corrected stem one.',
                    N'corrected-one-a', N'corrected-one-b', N'corrected-one-c', N'corrected-one-d', 'A',
                    N'The corrected explanation.');
            UPDATE Items SET LifecycleStatus = 'retired' WHERE ItemId = @item;
            """);

        var review = await CreateEngine().GetAttemptReviewAsync(attempt.AttemptId, userId);
        var question = review.Questions.Single(q => q.QuestionId == paper.FirstPositionId);

        Assert.Equal("Pinned stem one.", question.Stem);
        Assert.Equal(
            new[] { "Option A", "Option B", "Option C", "Option D" },
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
}
