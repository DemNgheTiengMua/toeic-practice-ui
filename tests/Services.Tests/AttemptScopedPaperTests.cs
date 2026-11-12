using System;
using System.Collections.Generic;
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
/// #274: <b>a sitting can hold its own paper, not the exam's shared roster.</b>
/// <para>
/// A placement sitting draws its own paper per learner, and the shared roster cannot hold one:
/// <c>UQ_ExamItems_Exam_Section_Order</c> allows one position per (exam, section, position), so a
/// second learner's draw has nowhere to go. The attempt-scoped pair
/// (<see cref="AttemptStageBundle"/> + <see cref="AttemptItemResponse"/>) is where it goes, and
/// <c>LoadSittingQuestionsAsync</c> resolves attempt-first so the take view, navigator, locks and
/// submission follow without a second mechanism.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "Two attempts were created" passes when both
/// were served the same positions, and "the take view has two questions" passes when it silently read
/// something other than the served list. So the assertions are on <b>which positions</b> each sitting
/// renders: the two sittings are served <i>disjoint</i> subsets of one roster and the test proves the
/// first question's stem is the one that subset names. The fixture also asserts the roster's row count
/// and the unique key directly, because criterion 2 is that the roster is never rewritten.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AttemptScopedPaperTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Criterion 1 (two attempts of the same exam, different positions), criterion 3 (the seam resolves
    /// attempt-first) and criterion 5 (a pool paper created, sat, answered and submitted under it).
    /// </summary>
    [Fact]
    public async Task Two_sittings_of_one_exam_are_served_different_positions_and_one_can_be_sat_and_submitted()
    {
        var name = $"EE274_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var paper = await SeedPoolPaperAsync(db, positionCount: 4);

            var engine = new AttemptEngine(db);

            // Two sittings of the SAME exam. A draws positions 1 and 3, B draws 2 and 4 - disjoint, so
            // "both got a paper" cannot be what makes this pass.
            var attemptA = await engine.CreateAttemptAsync(
                paper.UserId, paper.CertId, paper.ExamId, 60, Draw(paper, paper.Positions[0], paper.Positions[2]));
            var attemptB = await engine.CreateAttemptAsync(
                paper.UserId, paper.CertId, paper.ExamId, 60, Draw(paper, paper.Positions[1], paper.Positions[3]));

            Assert.NotEqual(attemptA.AttemptId, attemptB.AttemptId);

            // Criterion 2: the roster is not rewritten. Four positions existed before and four exist
            // after, and no position row was mutated - the sittings wrote their own tables only.
            Assert.Equal(4, await db.ExamItems.CountAsync(i => i.ExamId == paper.ExamId));
            Assert.Equal(4, paper.Positions.Select(p => p.ExamItemId).Distinct().Count());

            // The served rows are the two tables' contents, in sitting order.
            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var servedA = await verify.AttemptItemResponses.AsNoTracking()
                    .Where(r => r.AttemptId == attemptA.AttemptId)
                    .OrderBy(r => r.PositionNumber)
                    .Select(r => r.ExamItemId)
                    .ToListAsync();
                var servedB = await verify.AttemptItemResponses.AsNoTracking()
                    .Where(r => r.AttemptId == attemptB.AttemptId)
                    .OrderBy(r => r.PositionNumber)
                    .Select(r => r.ExamItemId)
                    .ToListAsync();

                Assert.Equal(
                    new[] { paper.Positions[0].ExamItemId, paper.Positions[2].ExamItemId },
                    servedA);
                Assert.Equal(
                    new[] { paper.Positions[1].ExamItemId, paper.Positions[3].ExamItemId },
                    servedB);
                Assert.Empty(servedA.Intersect(servedB));

                // The guarded fact: one bundle per skill per stage, written by its one owner.
                var bundles = await verify.AttemptStageBundles.AsNoTracking()
                    .Where(b => b.AttemptId == attemptA.AttemptId)
                    .ToListAsync();
                Assert.Single(bundles);
                Assert.Equal(DerivedFactWriters.AttemptPaper, bundles[0].WrittenByService);
                Assert.Equal(1, bundles[0].ProjectionVersion);
                Assert.Equal(CefrLevel.A2, bundles[0].BandLower);
                Assert.Equal(CefrLevel.B1, bundles[0].BandUpper);
            }

            // Criterion 3: the seam returns the attempt's OWN paper, not the roster's first two.
            var takeA = await engine.GetAttemptTakeAsync(attemptA.AttemptId, paper.UserId, questionDisplayOrder: 1);
            var takeB = await engine.GetAttemptTakeAsync(attemptB.AttemptId, paper.UserId, questionDisplayOrder: 1);

            Assert.Equal(2, takeA.TotalQuestions);
            Assert.Equal(2, takeB.TotalQuestions);
            Assert.StartsWith(paper.StemOf[0], takeA.CurrentQuestion.Stem, StringComparison.Ordinal);
            Assert.StartsWith(paper.StemOf[1], takeB.CurrentQuestion.Stem, StringComparison.Ordinal);
            // Both would read position 1's stem if the loader had fallen back to the roster.
            Assert.NotEqual(takeA.CurrentQuestion.Stem, takeB.CurrentQuestion.Stem);

            // ... and the ORDER is the served order, not the roster's: A was served position 3 first?
            // No - it was served 1 then 3, and the navigator must agree. Delete order is asserted by
            // navigating: question 2 of A is the THIRD roster position, not the second.
            var secondOfA = await engine.GetAttemptTakeAsync(attemptA.AttemptId, paper.UserId, questionDisplayOrder: 2);
            Assert.Equal(paper.Positions[2].ExamItemId, secondOfA.CurrentQuestion.QuestionId);
            Assert.Equal(paper.Positions[0].ExamItemId, takeA.CurrentQuestion.QuestionId);

            // Criterion 3's other half: an attempt with NO served paper keeps reading the shared
            // roster, which is every sitting that predates #274.
            var plainAttempt = await engine.CreateAttemptAsync(paper.UserId, paper.CertId, paper.ExamId, 60);
            var plain = await engine.GetAttemptTakeAsync(plainAttempt.AttemptId, paper.UserId, questionDisplayOrder: 1);
            Assert.Equal(4, plain.TotalQuestions);
            Assert.Empty(await db.AttemptItemResponses.Where(r => r.AttemptId == plainAttempt.AttemptId).ToListAsync());

            // Criterion 5: answer the served positions, then submit the attempt. The answer names the
            // roster position the sitting rendered, so nothing downstream had to learn a new id.
            foreach (var position in new[] { paper.Positions[0], paper.Positions[2] })
            {
                var page = await engine.GetAttemptTakeAsync(
                    attemptA.AttemptId, paper.UserId, questionDisplayOrder:
                        position.ExamItemId == paper.Positions[0].ExamItemId ? 1 : 2);
                var correctOption = page.CurrentQuestion.Options.Single(o => o.Label == "A").OptionId;
                await engine.SaveAnswerAsync(
                    attemptA.AttemptId, paper.UserId, page.CurrentQuestion.QuestionId, correctOption, isFlagged: false);
            }

            await using (var verify = DbContextFactory.Create(connectionString))
            {
                var answer = await verify.AttemptAnswers.AsNoTracking()
                    .SingleAsync(a => a.AttemptId == attemptA.AttemptId
                        && a.ExamItemId == paper.Positions[0].ExamItemId);
                Assert.Equal("A", answer.SelectedLabel);
                Assert.True(answer.IsCorrect, "the pinned version's key is A");
            }

            var graded = await engine.SubmitAttemptAsync(attemptA.AttemptId, paper.UserId, isAutoSubmit: false);
            Assert.Equal(AttemptStatus.Graded.ToCode(), graded.Attempt.Status);

            // The score is over the TWO positions served, not over the four the roster holds. With no
            // scale table the scaled score spreads the raw score over the range, so two-of-two reaches
            // the top (500) while a submission that had read the shared roster would divide by four and
            // land at 300. That difference is the whole assertion: a raw count cannot tell the two
            // apart, because the two positions it never served score nothing either way.
            var skillScore = Assert.Single(graded.SkillScores);
            Assert.Equal(2, skillScore.RawScore);
            Assert.Equal(500, skillScore.ScaledScore);

            // ... and the review enumerates the same two positions, in the same order.
            var review = await engine.GetAttemptReviewAsync(attemptA.AttemptId, paper.UserId);
            Assert.Equal(2, review.Questions.Count);
            Assert.Equal(paper.Positions[0].ExamItemId, review.Questions[0].QuestionId);
            Assert.Equal(paper.Positions[2].ExamItemId, review.Questions[1].QuestionId);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Criterion 3, in the direction that would break silently: a LATER draw for one sitting must not
    /// move the paper a learner is already sitting on, and the trigger is what refuses it. This is the
    /// ADR-0012 rule at the service boundary rather than only in raw SQL.
    /// </summary>
    [Fact]
    public async Task A_later_draw_cannot_restate_the_paper_a_sitting_is_already_on()
    {
        var name = $"EE274S_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var paper = await SeedPoolPaperAsync(db, positionCount: 2);

            var engine = new AttemptEngine(db);
            var attempt = await engine.CreateAttemptAsync(
                paper.UserId, paper.CertId, paper.ExamId, 60, Draw(paper, paper.Positions[0]));

            // A second writer names itself and moves the version: still refused, because the fact's
            // owner is AttemptEngine. This is the "second draw service" case, not a made-up name.
            var bundle = await db.AttemptStageBundles.SingleAsync(b => b.AttemptId == attempt.AttemptId);
            bundle.BandUpper = CefrLevel.C2;
            bundle.WrittenByService = "PlacementStageTwoDrawService";
            bundle.ProjectionVersion++;
            var refused = await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.Contains(
                "paper a sitting was served has exactly one writer",
                refused.InnerException?.Message ?? refused.Message,
                StringComparison.Ordinal);

            // And a replay at the same version is refused even by the owner. The declaration is marked
            // modified explicitly, exactly as the engine does, because EF omits a column whose value
            // did not change - and a value-only check would then let this through (ADR-0012).
            await using var replay = DbContextFactory.Create(connectionString);
            var stored = await replay.AttemptStageBundles.SingleAsync(b => b.AttemptId == attempt.AttemptId);
            stored.BandUpper = CefrLevel.C2;
            stored.WrittenByService = DerivedFactWriters.AttemptPaper;
            stored.ProjectionVersion = 1;
            replay.Entry(stored).Property(b => b.WrittenByService).IsModified = true;
            var replayRefusal = await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
                () => replay.SaveChangesAsync());
            Assert.Contains(
                "projection version must be newer",
                replayRefusal.InnerException?.Message ?? replayRefusal.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixture ---------------------------------------------------------------------------------

    private sealed record Paper(
        int UserId,
        int CertId,
        int ExamId,
        int SkillId,
        IReadOnlyList<PoolFixture.Position> Positions,
        IReadOnlyList<string> StemOf);

    /// <summary>
    /// The paper a draw heads over: one published pool paper with <paramref name="positionCount"/>
    /// positions, one skill, and the score scale a submission needs. Deliberately one skill, because
    /// the assertion that matters is <i>which of the paper's positions</i> a sitting was served.
    /// </summary>
    private static async Task<Paper> SeedPoolPaperAsync(CompanionDbContext db, int positionCount)
    {
        var code = Unique("C");
        var cert = new Certificate { Code = code, Name = $"Attempt paper cert {code}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, MinScaledScore = 100, MaxScaledScore = 500, ScoreStep = 50, DurationMinutes = 60,
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "R1",
            Name = "Incomplete Sentences", OptionCount = 4, ExpectedQuestionCount = positionCount,
            DisplayOrder = 1, NavigationMode = "FULL_SANDBOX",
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
        // NO ScoreScales rows, deliberately: with none, ProjectOntoScale spreads the raw score over the
        // skill's own range, so the SCALED score is a function of how many positions the paper
        // delivered. That is the one number that can tell "the sitting counted its own two positions"
        // apart from "it counted the roster's four" - both readings give raw 2, because the unanswered
        // positions score nothing either way.
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Attempt paper exam {code}", Status = "published",
            DurationMinutes = 60, Purpose = "placement", QuestionCount = positionCount,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var positions = new List<PoolFixture.Position>(positionCount);
        var stems = new List<string>(positionCount);
        for (var index = 0; index < positionCount; index++)
        {
            // A stem that names its own position: the assertion is "the sitting rendered THIS one".
            var stem = $"Position {index + 1} asked about depot _____.";
            positions.Add(await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, section.SectionId, "reading", stem, displayOrder: index + 1));
            stems.Add($"Position {index + 1} ");
        }

        var role = new Role { Code = Unique("paper274"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new User
        {
            RoleId = role.RoleId, Email = $"{Guid.NewGuid():N}@attemptpaper.test", PasswordHash = [1],
            FullName = "Attempt paper learner", ActiveCertId = cert.CertId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new Paper(user.UserId, cert.CertId, exam.ExamId, skill.SkillId, positions, stems);
    }

    /// <summary>
    /// The draw a placement sitting would produce: these positions, in this sitting order, and the band
    /// pair the draw routed this paper's one skill to.
    /// </summary>
    private static AttemptPaperDraw Draw(Paper paper, params PoolFixture.Position[] positions)
    {
        var ordered = positions
            .Select((position, index) => new AttemptPaperPosition(index + 1, position.ExamItemId))
            .ToList();
        return new AttemptPaperDraw(
            ordered,
            [new AttemptPaperBundle(paper.SkillId, StageNumber: 1, CefrLevel.A2, CefrLevel.B1)]);
    }
}
