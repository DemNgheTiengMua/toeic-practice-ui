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
/// #217 / PLC-18, PLC-19, PLC-34, PLC-35: submitting unfinished work, and a sitting that measured
/// something still producing a result.
/// <para>
/// <b>What would pass these tests while being wrong, per criterion:</b>
/// </para>
/// <list type="bullet">
/// <item>PLC-18 "unanswered scores incorrect": writing <c>IsCorrect = 0</c> on every blank passes every
/// score assertion on the result page and inflates the denominator of <c>vw_UserSectionAccuracyAll</c>.
/// So the assertion is made on the ROW - a blank stays NULL - and separately on the view's count.</item>
/// <item>PLC-19 "excluded from the overall": a not-measured skill stored as a zero score row drags the
/// certificate total down while looking like an ordinary measurement. So the assertion is that no
/// score row exists for the untouched skill, AND that the total equals the measured skill's score.</item>
/// <item>PLC-19 "abandoned, no result": setting <c>Status='abandoned'</c> while leaving yesterday's
/// TotalScore in place passes a status-only assertion. So the score and band are asserted null too.</item>
/// <item>PLC-35 "neither zero nor A1": a failed grading falling back to the certificate's floor is what
/// the ticket forbids; a weak test asserting "the level is null" passes both a correct
/// <c>not_measured</c> row and a fabricated <c>measured</c> row at PreA1. The status and the level are
/// asserted together.</item>
/// <item>PLC-34 "progressive": asserting the result page answers 200 passes a page that shows nothing.
/// So the objective skill's row is asserted `measured` in the same read that the productive one is
/// still `grading`.</item>
/// </list>
/// <para>Isolation: its own database, created and dropped, so seeding cannot leak into the suite.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementSubmissionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE217Sub_{Guid.NewGuid():N}"[..26];
    private string _connectionString = null!;

    public PlacementSubmissionTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private PlacementService Service(CompanionDbContext db) => new(db, new LearnerService(db));

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>Two objective skills (Listening, Reading) and one productive one (Writing), all on one
    /// placement paper, which is what "a partial result" needs three different answers from.</summary>
    private sealed record Fixture(
        int CertId, int ExamId,
        int ListeningSkillId, int ReadingSkillId, int WritingSkillId,
        int[] ListeningQuestions, int[] ReadingQuestions, int WritingQuestion,
        int CorrectOption);

    private async Task<Fixture> SeedAsync(string certCode)
    {
        await using var db = Db();

        db.Certificates.Add(new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "WRIT", Name = "Writing", Modality = "writing", DisplayOrder = 3, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 });
        await db.SaveChangesAsync();

        var skills = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        db.Sections.AddRange(
            new Section { SkillId = skills["LIST"], CertId = certId, Modality = "listening", Code = "L1", Name = "Listening", ExpectedQuestionCount = 2, DisplayOrder = 1 },
            new Section { SkillId = skills["READ"], CertId = certId, Modality = "reading", Code = "R1", Name = "Reading", ExpectedQuestionCount = 2, DisplayOrder = 2 },
            new Section { SkillId = skills["WRIT"], CertId = certId, Modality = "writing", Code = "W1", Name = "Writing", ExpectedQuestionCount = 1, DisplayOrder = 3 });
        await db.SaveChangesAsync();

        db.Exams.Add(new Exam
        {
            CertId = certId,
            Name = "Placement 217",
            Status = "published",
            DurationMinutes = 60,
            Purpose = "placement",
            QuestionCount = 5,
        });
        await db.SaveChangesAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();

        var sections = await db.Sections.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SectionId);
        var listeningIds = new int[2];
        var readingIds = new int[2];
        var writingId = 0;
        var correctOption = 0;

        foreach (var (code, count, order) in new[] { ("L1", 2, 1), ("R1", 2, 2), ("W1", 1, 3) })
        {
            var group = new QuestionGroup { ExamId = examId, CertId = certId, SectionId = sections[code], DisplayOrder = order };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            var skillId = code == "L1" ? skills["LIST"] : code == "R1" ? skills["READ"] : skills["WRIT"];
            for (var index = 0; index < count; index++)
            {
                var question = new Question
                {
                    GroupId = group.GroupId,
                    ExamId = examId,
                    SectionId = sections[code],
                    SkillId = skillId,
                    Stem = $"{code}-{index + 1}",
                    CefrLevel = CefrLevel.B1,
                    QuestionType = code == "W1" ? "free_response" : "mcq",
                    DisplayOrder = order * 10 + index,
                };
                db.Questions.Add(question);
                await db.SaveChangesAsync();

                if (code == "W1")
                {
                    writingId = question.QuestionId;
                    continue;
                }

                db.QuestionOptions.AddRange(
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true },
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false });
                await db.SaveChangesAsync();

                if (code == "L1") listeningIds[index] = question.QuestionId;
                else readingIds[index] = question.QuestionId;
            }
        }

        // a scale, so a measured skill converts to a number instead of refusing
        var scaleRows = await db.Skills.Where(s => s.CertId == certId)
            .Select(s => s.SkillId).ToListAsync();
        foreach (var skillId in scaleRows)
        {
            db.ScoreScales.AddRange(
                new ScoreScale { CertId = certId, SkillId = skillId, RawScore = 0, ScaledScore = 0 },
                new ScoreScale { CertId = certId, SkillId = skillId, RawScore = 2, ScaledScore = 200 });
        }

        db.ScoreBands.AddRange(
            new ScoreBand { CertId = certId, Code = "A2", Name = "Elementary", MinTotal = 0, MaxTotal = 199, DisplayOrder = 1 },
            new ScoreBand { CertId = certId, Code = "B1", Name = "Intermediate", MinTotal = 200, MaxTotal = 400, DisplayOrder = 2 });
        await db.SaveChangesAsync();

        // #259 / PRN-03: a placement needs a CUT-SCORE table, not only a band table. Without one the
        // projection cannot place a score, and before #259 the writer papered over that by parsing the
        // band code as a CEFR level - which is the crosswalk the rule forbids. A fixture with bands and
        // no cut table was asserting that behaviour, so it now carries the table a real certificate has.
        db.CefrCutScoreVersions.Add(new CefrCutScoreVersion
        {
            CertId = certId,
            TableVersion = 1,
            ReportsSkillsSeparately = false,
            Source = "fixture",
            PublishedAt = new DateOnly(2026, 1, 1),
        });
        foreach (var skillId in scaleRows)
        {
            db.CefrCutScores.AddRange(
                new CefrCutScore { CertId = certId, TableVersion = 1, SkillId = skillId, CefrLevel = "A2", RowKind = "cut", MinScaled = 0, MaxScaled = 199 },
                new CefrCutScore { CertId = certId, TableVersion = 1, SkillId = skillId, CefrLevel = "B1", RowKind = "cut", MinScaled = 200, MaxScaled = 400 });
        }

        await db.SaveChangesAsync();

        correctOption = await db.QuestionOptions
            .Where(o => o.QuestionId == listeningIds[0] && o.IsCorrect)
            .Select(o => o.OptionId).SingleAsync();

        return new Fixture(certId, examId, skills["LIST"], skills["READ"], skills["WRIT"],
            listeningIds, readingIds, writingId, correctOption);
    }

    private async Task<int> SeedLearnerAsync(int certId, int startingCredits = 0)
    {
        await using var db = Db();
        if (!await db.Roles.AnyAsync(r => r.Code == "learner"))
        {
            db.Roles.Add(new Role { Code = "learner", Name = "Learner" });
            await db.SaveChangesAsync();
        }

        var roleId = await db.Roles.Where(r => r.Code == "learner").Select(r => r.RoleId).SingleAsync();
        var email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = roleId, Email = email, PasswordHash = [0x01], ActiveCertId = certId });
        await db.SaveChangesAsync();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            // A balance, so a plausible "the retry charged a credit" mutation is STORABLE here. Without
            // one the ledger triggers would refuse the wrong write for their own reasons, and the
            // no-credit assertion below would pass for the wrong reason.
            if (!await db.Roles.AnyAsync(r => r.Code == "admin"))
            {
                db.Roles.Add(new Role { Code = "admin", Name = "Admin" });
                await db.SaveChangesAsync();
            }
            var adminRoleId = await db.Roles.Where(r => r.Code == "admin").Select(r => r.RoleId).SingleAsync();
            var adminEmail = $"admin-{Guid.NewGuid():N}@test.local";
            db.Users.Add(new User { RoleId = adminRoleId, Email = adminEmail, PasswordHash = [0x01] });
            await db.SaveChangesAsync();
            var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.UserId).SingleAsync();

            db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId,
                Reason = "admin_grant",
                Delta = startingCredits,
                AdminUserId = adminId,
                Note = "Test setup credits",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return userId;
    }

    private async Task<PlacementTestInfo> StartAsync(int userId, Fixture fixture, CompanionDbContext db)
    {
        var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
        return info;
    }

    /// <summary>Answers one objective question with ITS OWN correct option, crossing the one-way skill
    /// lock first. The lock refuses an answer for a skill the attempt has not reached, which is the
    /// engine's rule, not the thing under test; the option must belong to the question, which is what
    /// <c>FK_AttemptAnswers_OptionQuestion</c> enforces.</summary>
    private async Task<int> AnswerCorrectlyAsync(int attemptId, int questionId)
    {
        await using var db = Db();
        var skillId = await db.Questions.Where(q => q.QuestionId == questionId).Select(q => q.SkillId).SingleAsync();
        var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        attempt.ActiveSkillId = skillId;
        attempt.PhaseDeadlineAt = DateTime.UtcNow.AddMinutes(30);
        await db.SaveChangesAsync();

        var optionId = await db.QuestionOptions
            .Where(o => o.QuestionId == questionId && o.IsCorrect == true)
            .Select(o => o.OptionId).SingleAsync();

        var answer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId);
        answer.SelectedOptionId = optionId;
        await db.SaveChangesAsync();
        return optionId;
    }

    /// <summary>Answers one objective question with ITS OWN wrong option, under the same lock rule.</summary>
    private async Task AnswerIncorrectlyAsync(int attemptId, int questionId)
    {
        await using var db = Db();
        var skillId = await db.Questions.Where(q => q.QuestionId == questionId).Select(q => q.SkillId).SingleAsync();
        var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        attempt.ActiveSkillId = skillId;
        attempt.PhaseDeadlineAt = DateTime.UtcNow.AddMinutes(30);
        await db.SaveChangesAsync();

        var optionId = await db.QuestionOptions
            .Where(o => o.QuestionId == questionId && o.IsCorrect == false)
            .Select(o => o.OptionId).SingleAsync();

        var answer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId);
        answer.SelectedOptionId = optionId;
        await db.SaveChangesAsync();
    }

    private async Task<int> AttemptIdAsync(int placementId)
    {
        await using var db = Db();
        return await db.PlacementTests.Where(p => p.PlacementId == placementId).Select(p => p.AttemptId!.Value).SingleAsync();
    }

    /// <summary>#217 / PLC-35: a grader that always fails, so the settle path runs for real.</summary>
    private sealed class FailingGrader : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiGradingResult(
                Success: false,
                FailureReason: "The model timed out.",
                Model: "failing-grader",
                Scores: []));
    }

    // ---------------------------------------------------------------------------------------------
    // PLC-18: unanswered objective items score as incorrect, without becoming "answered"
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One of Listening's two items answered, the other left blank; Reading and Writing untouched.
    /// <para>
    /// <b>Three facts, each of which a weaker test would miss.</b> The blank's <c>IsCorrect</c> is NULL
    /// (not 0 - the view counts it). The Listening skill's <c>RawScore</c> is 1 of 2, so the blank cost
    /// a mark. And the certificate total is the ONE measured skill, not a sum with a zero in it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unanswered_item_is_scored_incorrect_without_being_recorded_as_answered()
    {
        var code = Unique("T217A");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[0]);

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var blank = await verify.AttemptAnswers.AsNoTracking()
            .SingleAsync(a => a.AttemptId == attemptId && a.QuestionId == fixture.ListeningQuestions[1]);
        Assert.Null(blank.SelectedOptionId);
        // The wrong-but-passing input: `IsCorrect = false` here passes every total on the page and
        // inflates vw_UserSectionAccuracyAll. The blank must stay unjudged.
        Assert.Null(blank.IsCorrect);

        var listening = await verify.AttemptSkillScores.AsNoTracking()
            .SingleAsync(s => s.AttemptId == attemptId && s.SkillId == fixture.ListeningSkillId);
        Assert.Equal(1, listening.RawScore);     // 1 right of 2: the blank cost a mark

        // and the pool accuracy view sees ONE answered item in Listening, not two
        var listeningSectionId = await verify.Sections
            .Where(s => s.SkillId == fixture.ListeningSkillId)
            .Select(s => s.SectionId).SingleAsync();
        var accuracy = await verify.vw_UserSectionAccuracyAlls.AsNoTracking()
            .SingleAsync(v => v.UserId == userId && v.SectionId == listeningSectionId);
        Assert.Equal(1, accuracy.Answered);
        Assert.Equal(1, accuracy.Correct);
    }

    // ---------------------------------------------------------------------------------------------
    // PLC-19: one measured skill produces a partial result, and the rest are excluded from the overall
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Only Listening answered; Reading and Writing untouched. Listening is measured, the other two are
    /// <c>not_measured · candidate_skipped</c>, and the placement is <c>done</c> with NO completion
    /// failure because something WAS measured.
    /// <para>
    /// <b>The wrong-but-passing input:</b> a zero score row for each untouched skill. The placement
    /// would still be `done` with measured rows, and the not-measured status would still be right - but
    /// the total would be Listening plus two zeros. So the total is asserted to be exactly the measured
    /// skill's scaled score, and no score row may exist for the other two.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_sitting_with_one_measured_skill_completes_with_a_partial_result_the_rest_excluded()
    {
        var code = Unique("T217B");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[0]);
        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[1]);

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var rows = await verify.PlacementSkillResults.AsNoTracking()
            .Where(r => r.PlacementId == placementId)
            .ToDictionaryAsync(r => r.SkillId);

        Assert.Equal(PlacementResultStatus.Measured, rows[fixture.ListeningSkillId].ResultStatus);
        foreach (var skillId in new[] { fixture.ReadingSkillId, fixture.WritingSkillId })
        {
            Assert.Equal(PlacementResultStatus.NotMeasured, rows[skillId].ResultStatus);
            Assert.Equal(PlacementFailureReason.CandidateSkipped, rows[skillId].FailureReason);
            // neither zero nor A1: no level, and NOT PreA1
            Assert.Null(rows[skillId].CefrLevel);
        }

        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("done", placement.Status);
        Assert.Null(placement.CompletionFailureReason);

        // "excluded from the overall", asserted the way a zero would fail: exactly one score row, and
        // the total is that row's scaled score rather than a sum with two zeros in it.
        var scores = await verify.AttemptSkillScores.AsNoTracking()
            .Where(s => s.AttemptId == attemptId).ToListAsync();
        Assert.Single(scores);
        Assert.Equal(fixture.ListeningSkillId, scores[0].SkillId);
        Assert.Equal(scores[0].ScaledScore, placement.TotalScore);
        Assert.NotEqual(0, placement.TotalScore);
    }

    /// <summary>
    /// A skill where the learner answered but got everything wrong is a real measurement at the floor,
    /// not `not_measured`. This is the pair to the test above: excluding untouched skills must not
    /// exclude a zero-scoring attempt, or "excluded" becomes "hidden a bad result".
    /// </summary>
    [Fact]
    public async Task A_skill_answered_entirely_wrong_is_measured_at_zero_and_kept()
    {
        var code = Unique("T217C");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        // both Listening items answered, both wrong
        foreach (var questionId in fixture.ListeningQuestions)
        {
            await AnswerIncorrectlyAsync(attemptId, questionId);
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var row = await verify.PlacementSkillResults.AsNoTracking()
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.ListeningSkillId);
        Assert.Equal(PlacementResultStatus.Measured, row.ResultStatus);
        Assert.NotNull(row.CefrLevel);

        var score = await verify.AttemptSkillScores.AsNoTracking()
            .SingleAsync(s => s.AttemptId == attemptId && s.SkillId == fixture.ListeningSkillId);
        Assert.Equal(0, score.RawScore);
    }

    // ---------------------------------------------------------------------------------------------
    // PLC-19: nothing measurable is abandoned with no result
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A sitting submitted with nothing answered and nothing drafted.
    /// <para>
    /// <b>The wrong-but-passing input:</b> setting only the status. The status is asserted, and so are
    /// the score and band - a leftover figure from a previous write is exactly the fabricated result
    /// the ticket forbids. The attempt carries none either, so a page reading the attempt cannot
    /// invent one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_sitting_that_measured_nothing_is_abandoned_with_no_result_at_all()
    {
        var code = Unique("T217D");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db())
        {
            var started = await StartAsync(userId, fixture, db);
            placementId = started.PlacementId;
            // a stale figure, to prove the writer clears it rather than leaving it behind
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == started.AttemptId);
            attempt.TotalScore = 250;
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            await db.SaveChangesAsync();
        }
        var attemptId = await AttemptIdAsync(placementId);

        await using (var db = Db())
        {
            var result = await Assert.ThrowsAsync<PlacementException>(
                () => Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false));
            Assert.Equal(PlacementException.PlacementAbandoned, result.Code);
        }

        await using var verify = Db();
        var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
        Assert.Equal("abandoned", placement.Status);
        Assert.Null(placement.TotalScore);
        Assert.Null(placement.ResultBandCode);
        Assert.NotNull(placement.CompletionFailureReason);

        var attemptRow = await verify.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        Assert.Null(attemptRow.TotalScore);
        Assert.Null(attemptRow.BandCode);

        // the stale figure is gone, so a page reading the attempt cannot present one
        Assert.Empty(await verify.AttemptSkillScores.AsNoTracking().Where(s => s.AttemptId == attemptId).ToListAsync());

        // and the result read refuses rather than handing back a fabricated page
        var noResult = await Assert.ThrowsAsync<PlacementException>(
            () => Service(verify).GetPlacementResultAsync(userId, placementId));
        Assert.Equal(PlacementException.PlacementAbandoned, noResult.Code);
    }

    // ---------------------------------------------------------------------------------------------
    // PLC-35: a failed grading is not zero and not A1, and the retry charges nothing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Writing's only task failed to grade. The skill is <c>not_measured · ai_grading_failed</c>, has
    /// no level at all, and the placement still completes on the measured Listening half.
    /// <para>
    /// <b>The wrong-but-passing input:</b> a writer that falls back to <c>LevelFromBand(null)</c> stores
    /// PreA1 with status `measured`; a test that only asserted "the level is not B1" would accept it.
    /// The status, the null level and the absence of any score row are asserted together.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_failed_grading_is_neither_zero_nor_A1_and_the_retry_writes_no_ledger_row()
    {
        var code = Unique("T217E");
        var fixture = await SeedAsync(code);
        // credits, so a "the retry charged" mutation is storable and this test can see it
        var userId = await SeedLearnerAsync(fixture.CertId, startingCredits: 5);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        // Listening answered; Writing submitted with a draft, then its grading fails.
        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[0]);
        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[1]);

        await using (var db = Db())
        {
            var writing = await db.Questions.SingleAsync(q => q.QuestionId == fixture.WritingQuestion);
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            attempt.ActiveSkillId = writing.SkillId;
            await db.SaveChangesAsync();

            await Service(db).SaveDraftAsync(userId, placementId, fixture.WritingQuestion,
                "A response long enough to be handed to the grader.");
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using (var db = Db())
        {
            // The worker runs for real: the grader fails, and the PROCESSOR is what settles the task.
            // Driving that (rather than hand-writing the rows) is what proves PLC-35's "a failed task
            // does not void the skill" survives the real pipeline: the response must leave
            // `pending_ai`, or the attempt could never settle and the learner would wait forever.
            var processor = new AiGradingProcessor(db, new FailingGrader());
            await processor.ProcessPendingGradingsAsync(10);
        }

        await using (var db = Db())
        {
            // and the attempt settles through the engine's own finalizer
            await new AttemptEngine(db).FinalizeAiGradedAttemptAsync(attemptId);
        }

        await using (var verify = Db())
        {
            var writing = await verify.PlacementSkillResults.AsNoTracking()
                .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.WritingSkillId);
            Assert.Equal(PlacementResultStatus.NotMeasured, writing.ResultStatus);
            Assert.Equal(PlacementFailureReason.AiGradingFailed, writing.FailureReason);
            Assert.Null(writing.CefrLevel);                     // not A1, not PreA1 - nothing
            Assert.Null(writing.PositionInBand);
            Assert.Null(writing.MeasuredAt);

            // and no fabricated score row for the failed skill
            Assert.DoesNotContain(await verify.AttemptSkillScores.AsNoTracking()
                .Where(s => s.AttemptId == attemptId).ToListAsync(), s => s.SkillId == fixture.WritingSkillId);

            // the objective half still measured: a partial result, and the placement is done
            var listening = await verify.PlacementSkillResults.AsNoTracking()
                .SingleAsync(r => r.PlacementId == placementId && r.SkillId == fixture.ListeningSkillId);
            Assert.Equal(PlacementResultStatus.Measured, listening.ResultStatus);

            var placement = await verify.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == placementId);
            Assert.Equal("done", placement.Status);
            Assert.Null(placement.CompletionFailureReason);
        }

        // the retry: re-queues the failed task, and charges nothing
        long ledgerRowsBefore;
        int balanceBefore;
        await using (var before = Db())
        {
            ledgerRowsBefore = await before.CreditTransactions.LongCountAsync();
            balanceBefore = await before.vw_UserCreditBalances
                .Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync() ?? 0;
        }

        await using (var db = Db())
        {
            var outcome = await Service(db).RetryGradingAsync(userId, placementId);
            Assert.Equal(1, outcome.RequeuedCount);
        }

        await using (var after = Db())
        {
            var grading = await after.AiGradings.AsNoTracking()
                .SingleAsync(g => g.FreeResponse.AttemptId == attemptId);
            Assert.Equal(AiGradingStatus.Pending.ToCode(), grading.Status);
            Assert.Equal(2, grading.AttemptCount);
            Assert.Null(grading.FailureReason);

            // "the retry writes no ledger row" - the whole cost of this criterion. The balance is
            // asserted too: a row count alone passes a mutation that moves the wallet without adding
            // a row, and the learner's number is what "charged" actually means.
            Assert.Equal(ledgerRowsBefore, await after.CreditTransactions.LongCountAsync());
            Assert.Equal(balanceBefore, await after.vw_UserCreditBalances
                .Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync() ?? 0);
        }
    }

    /// <summary>A retry with no failure to retry is refused with its own code, not a silent success.</summary>
    [Fact]
    public async Task A_retry_with_nothing_failed_is_refused()
    {
        var code = Unique("T217F");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db())
        {
            placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        }

        await using var verify = Db();
        var error = await Assert.ThrowsAsync<PlacementException>(
            () => Service(verify).RetryGradingAsync(userId, placementId));
        Assert.Equal(PlacementException.RetryNotAllowed, error.Code);
    }

    // ---------------------------------------------------------------------------------------------
    // PLC-34: results appear progressively
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Submitted with a draft in the AI queue: the result read answers, the objective skill is already
    /// <c>measured</c>, and Writing is <c>grading</c> - not "not finished", which is what it used to
    /// say while bouncing the learner back to a sitting they had already submitted.
    /// </summary>
    [Fact]
    public async Task The_objective_half_is_visible_immediately_while_the_productive_half_is_graded()
    {
        var code = Unique("T217G");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[0]);
        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[1]);

        await using (var db = Db())
        {
            var writing = await db.Questions.SingleAsync(q => q.QuestionId == fixture.WritingQuestion);
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            attempt.ActiveSkillId = writing.SkillId;
            await db.SaveChangesAsync();
            await Service(db).SaveDraftAsync(userId, placementId, fixture.WritingQuestion,
                "A response long enough to be handed to the grader.");
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var verify = Db();
        var interim = await Service(verify).GetPlacementResultAsync(userId, placementId);

        // the page is readable, it says grading, and the objective half is already there
        Assert.False(interim.IsGraded);
        Assert.Contains(interim.SkillResults!, r => r.SkillId == fixture.ListeningSkillId
            && r.Status == PlacementResultStatus.Measured);
        Assert.Contains(interim.SkillResults!, r => r.SkillId == fixture.WritingSkillId
            && r.Status == PlacementResultStatus.Grading);
        Assert.Contains(interim.SkillScores, s => s.SkillId == fixture.ListeningSkillId);
    }

    /// <summary>
    /// The same read twice writes nothing: PLC-16 and PLC-34 together. A result page that re-queued a
    /// job on each poll would dispatch the grader forever.
    /// </summary>
    [Fact]
    public async Task Reading_the_progressive_result_writes_no_grading_row_and_moves_no_score()
    {
        var code = Unique("T217H");
        var fixture = await SeedAsync(code);
        var userId = await SeedLearnerAsync(fixture.CertId);
        int placementId;
        await using (var db = Db()) placementId = (await StartAsync(userId, fixture, db)).PlacementId;
        var attemptId = await AttemptIdAsync(placementId);

        await AnswerCorrectlyAsync(attemptId, fixture.ListeningQuestions[0]);
        await using (var db = Db())
        {
            var writing = await db.Questions.SingleAsync(q => q.QuestionId == fixture.WritingQuestion);
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            attempt.ActiveSkillId = writing.SkillId;
            await db.SaveChangesAsync();
            await Service(db).SaveDraftAsync(userId, placementId, fixture.WritingQuestion,
                "A response long enough to be handed to the grader.");
        }
        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        int gradingsBefore;
        int? scoreBefore;
        int versionBefore;
        await using (var db = Db())
        {
            gradingsBefore = await db.AiGradings.CountAsync(g => g.FreeResponse.AttemptId == attemptId);
            var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
            scoreBefore = attempt.TotalScore;
            versionBefore = attempt.ProjectionVersion;
        }

        await using (var db = Db())
        {
            await Service(db).GetPlacementResultAsync(userId, placementId);
            await Service(db).GetPlacementResultAsync(userId, placementId);
            await Service(db).GetGradingStatusAsync(userId, placementId);
        }

        await using var verify = Db();
        Assert.Equal(gradingsBefore, await verify.AiGradings.CountAsync(g => g.FreeResponse.AttemptId == attemptId));
        var after = await verify.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal(scoreBefore, after.TotalScore);
        Assert.Equal(versionBefore, after.ProjectionVersion);
    }
}
