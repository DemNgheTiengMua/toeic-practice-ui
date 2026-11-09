using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #214 / PLC-10, PLC-12: a placement sitting has no clock, and it is still the one attempt engine that
/// runs it.
/// <para>
/// <b>What would pass these tests while being wrong.</b> "No countdown" could be satisfied by leaving
/// the countdown on the page and merely not auto-submitting - so both halves are asserted: the attempt's
/// own deadline is the 72-hour cap rather than the exam's 30 minutes, and it carries no phase deadline.
/// "One engine" could be satisfied by a comment, so the sitting is driven through the real engine and
/// its answer, position and scoring are read back from <c>AttemptAnswers</c> and
/// <c>AttemptSkillScores</c> - the tables only the engine writes.
/// </para>
/// <para>Isolation: its own database, created and dropped.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementSittingHasNoCountdownTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE214Sit_{Guid.NewGuid():N}"[..26];
    private string _connectionString = null!;

    public PlacementSittingHasNoCountdownTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private sealed record Fixture(int CertId, int ExamId, int QuestionId, int CorrectOptionId);

    /// <summary>
    /// A two-skill certificate and one placement exam of 30 minutes - deliberately short, because 30
    /// minutes is what a countdown would expire at and a 72-hour cap never does.
    /// </summary>
    private async Task<Fixture> SeedAsync(string code)
    {
        await using var db = Db();
        db.Certificates.Add(new Certificate { Code = code, Name = $"Cert {code}", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 });
        await db.SaveChangesAsync();

        db.Sections.AddRange(
            new Section { SkillId = db.Skills.Where(s => s.Code == "LIST" && s.CertId == certId).Select(s => s.SkillId).Single(), CertId = certId, Modality = "listening", Code = "L1", Name = "Listening", ExpectedQuestionCount = 1, DisplayOrder = 1 },
            new Section { SkillId = db.Skills.Where(s => s.Code == "READ" && s.CertId == certId).Select(s => s.SkillId).Single(), CertId = certId, Modality = "reading", Code = "R1", Name = "Reading", ExpectedQuestionCount = 1, DisplayOrder = 2 });
        await db.SaveChangesAsync();

        db.Exams.Add(new Exam
        {
            CertId = certId,
            Name = "Placement 30 minutes",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "placement",
            QuestionCount = 2,
        });
        await db.SaveChangesAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();

        foreach (var (sectionCode, order) in new[] { ("L1", 1), ("R1", 2) })
        {
            var section = await db.Sections.Where(s => s.Code == sectionCode && s.CertId == certId).SingleAsync();
            var group = new QuestionGroup { ExamId = examId, CertId = certId, SectionId = section.SectionId, DisplayOrder = order };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            var question = new Question
            {
                GroupId = group.GroupId,
                ExamId = examId,
                SectionId = section.SectionId,
                SkillId = section.SkillId,
                Stem = $"Q{order}",
                CefrLevel = CefrLevel.A2,
                QuestionType = "mcq",
                DisplayOrder = order,
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync();

            db.QuestionOptions.AddRange(
                new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true },
                new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false });
        }

        db.ScoreBands.Add(new ScoreBand { CertId = certId, Code = "A2", Name = "Elementary", MinTotal = 0, MaxTotal = 200, DisplayOrder = 1 });
        await db.SaveChangesAsync();

        // #259 / PRN-03: a cut-score table is what places a score. Without one the writer used to parse
        // the band code as a CEFR level - the crosswalk the rule forbids.
        db.CefrCutScoreVersions.Add(new CefrCutScoreVersion
        {
            CertId = certId,
            TableVersion = 1,
            ReportsSkillsSeparately = false,
            Source = "fixture",
            PublishedAt = new DateOnly(2026, 1, 1),
        });
        foreach (var skillId in await db.Skills.Where(s => s.CertId == certId).Select(s => s.SkillId).ToListAsync())
        {
            db.CefrCutScores.Add(new CefrCutScore
            {
                CertId = certId, TableVersion = 1, SkillId = skillId,
                CefrLevel = "A2", RowKind = "cut", MinScaled = 0, MaxScaled = 500,
            });
        }

        await db.SaveChangesAsync();

        var questionId = await db.Questions.Where(q => q.ExamId == examId).OrderBy(q => q.DisplayOrder)
            .Select(q => q.QuestionId).FirstAsync();
        var correctOption = await db.QuestionOptions.Where(o => o.QuestionId == questionId && o.IsCorrect)
            .Select(o => o.OptionId).SingleAsync();

        return new Fixture(certId, examId, questionId, correctOption);
    }

    private async Task<int> SeedLearnerAsync(int certId)
    {
        await using var db = Db();
        db.Roles.Add(new Role { Code = "learner-214", Name = "Learner 214" });
        await db.SaveChangesAsync();
        var roleId = await db.Roles.Where(r => r.Code == "learner-214").Select(r => r.RoleId).SingleAsync();

        var email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(new User { RoleId = roleId, Email = email, PasswordHash = [0x01], ActiveCertId = certId });
        await db.SaveChangesAsync();
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private PlacementService Service(CompanionDbContext db) => new(db, new LearnerService(db));

    [Fact]
    public async Task A_placement_deadline_is_the_outer_bound_and_it_carries_no_phase_clock()
    {
        var fixture = await SeedAsync(Unique("NC1"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        int attemptId;
        await using (var db = Db())
        {
            var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
            attemptId = info.AttemptId;
        }

        await using var read = Db();
        var attempt = await read.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        var exam = await read.Exams.SingleAsync(e => e.ExamId == fixture.ExamId);

        // The exam says 30 minutes; the attempt says 48 hours. `Exams.DurationMinutes` is a mock's wall
        // clock, and reusing it here is exactly the countdown PLC-12 forbids. Asserting both is what
        // stops a "48 hours" that came from a 48-minute exam.
        //
        // #216 supersedes #214's fixed 72-hour expectation: PLC-13 is
        // min(last_activity + 48 h, started_at + 72 h), and a sitting nothing has been written to yet
        // is measured from its start, so it opens at 48 h with 72 h as the cap a later stamp is
        // clamped against.
        Assert.Equal(30, exam.DurationMinutes);
        Assert.Equal(PlacementSitting.Inactivity, attempt.ExpiresAt - attempt.StartedAt);
        Assert.True(attempt.ExpiresAt - attempt.StartedAt < PlacementSitting.MaxLifetime);
        Assert.Null(attempt.PhaseDeadlineAt);
    }

    /// <summary>
    /// Passing the preflight does not re-arm a countdown. The fixture must actually <b>require</b> a
    /// preflight - a listening section with audio - or <c>PreflightVerifiedAt</c> is already set at
    /// create time and this branch is never reached. That was the first version of this test: it passed
    /// while the branch it names was mutated to re-arm the clock, which is a test asserting nothing.
    /// </summary>
    [Fact]
    public async Task Passing_the_preflight_does_not_re_arm_a_countdown()
    {
        var fixture = await SeedAsync(Unique("NC2"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        await using (var db = Db())
        {
            // Audio is what puts the preflight gate in front of the sitting, and the gate is the second
            // place a deadline is stamped.
            await db.Database.ExecuteSqlAsync($"""
                UPDATE QuestionGroups SET AudioPath = '/audio/214-preflight.mp3'
                WHERE ExamId = {fixture.ExamId}
                """);
        }

        int attemptId;
        await using (var db = Db())
        {
            attemptId = (await Service(db).StartPlacementTestAsync(userId, fixture.ExamId)).AttemptId;
        }

        // The preflight gate is the second place a deadline is stamped, and it used to reset the clock
        // to now + DurationMinutes - undoing the create path's work in one line.
        await using (var db = Db())
        {
            var engine = new AttemptEngine(db);
            Assert.True(await engine.RequiresPreflightAsync(fixture.ExamId));
            await engine.VerifyPreflightAsync(attemptId, userId);
        }

        await using var read = Db();
        var attempt = await read.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        Assert.NotNull(attempt.PreflightVerifiedAt);
        // #216: the gate re-stamps the deadline to now + 48 h (PLC-13's shorter window), never to the
        // exam's 30-minute clock - and it never past the 72-hour cap.
        Assert.Equal(PlacementSitting.Inactivity, attempt.ExpiresAt - attempt.StartedAt);
        Assert.True(attempt.ExpiresAt - attempt.StartedAt <= PlacementSitting.MaxLifetime);
        Assert.Null(attempt.PhaseDeadlineAt);
    }

    /// <summary>
    /// A mock keeps its clock. The trap is a policy applied to <c>AttemptEngine</c> globally - every
    /// mock exam would silently become untimed, and no test above would notice.
    /// </summary>
    [Fact]
    public async Task A_mock_exam_keeps_its_own_clock()
    {
        var fixture = await SeedAsync(Unique("NC3"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        await using (var db = Db())
        {
            // SQL, not EF: Purpose is part of UQ_Exams_Id_Cert_Purpose, and EF refuses to modify a key
            // property. The fixture is changing what the paper IS, which is exactly what that index
            // protects.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE Exams SET Purpose = 'mock' WHERE ExamId = {fixture.ExamId}");
        }

        int attemptId;
        await using (var db = Db())
        {
            attemptId = (await new AttemptEngine(db).CreateAttemptAsync(
                userId, fixture.CertId, fixture.ExamId, 30)).AttemptId;
        }

        await using var read = Db();
        var attempt = await read.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal(TimeSpan.FromMinutes(30), attempt.ExpiresAt - attempt.StartedAt);
        Assert.NotNull(attempt.PhaseDeadlineAt);
    }

    /// <summary>
    /// PLC-10: the existing engine performs the sitting, and there is no second one. The proof is the
    /// tables only the engine writes - an answer row it created when the attempt started, the option
    /// it recorded, and the skill scores it derived on submit. A second engine would have had to
    /// reproduce all three.
    /// <para>
    /// The input that would pass a weaker test: a placement that <i>also</i> had a parallel sitting
    /// engine for the parts the shared one does not cover. So the implementation set is asserted too -
    /// <see cref="IAttemptEngine"/> has exactly one implementation in this assembly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_attempt_engine_itself_sits_the_placement()
    {
        var implementations = typeof(IAttemptEngine).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IAttemptEngine).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();
        Assert.Equal(["AttemptEngine"], implementations);

        var fixture = await SeedAsync(Unique("NC4"));
        var userId = await SeedLearnerAsync(fixture.CertId);

        int placementId;
        await using (var db = Db())
        {
            var info = await Service(db).StartPlacementTestAsync(userId, fixture.ExamId);
            placementId = info.PlacementId;

            // The engine pre-creates one answer row per objective question when the attempt starts.
            var answers = await db.AttemptAnswers.CountAsync(a => a.AttemptId == info.AttemptId);
            Assert.Equal(2, answers);
        }

        await using (var db = Db())
        {
            var saved = await Service(db).SaveAnswerAsync(userId, placementId, fixture.QuestionId, fixture.CorrectOptionId, isFlagged: false);
            Assert.Equal(fixture.CorrectOptionId, saved.SelectedOptionId);
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        await using var read = Db();
        var placement = await read.PlacementTests.SingleAsync(p => p.PlacementId == placementId);
        var attempt = await read.ExamAttempts.SingleAsync(a => a.AttemptId == placement.AttemptId);

        // The engine scored it: a graded attempt with a total, and a skill score row for the skill it
        // MEASURED. #217 / PLC-19 changed the count from 2 to 1: only Listening was answered, so only
        // Listening is measured and only Listening gets a score row. The untouched Reading skill is
        // `not_measured` on the placement, not a zero row dragging the total down - which is the whole
        // point of "excluded from the overall". The previous expectation of 2 encoded the zero.
        Assert.Equal("graded", attempt.Status);
        Assert.NotNull(attempt.TotalScore);
        var scores = await read.AttemptSkillScores.Where(s => s.AttemptId == attempt.AttemptId).ToListAsync();
        Assert.Single(scores);
        Assert.Equal(fixture.QuestionId, await read.Questions
            .Where(q => q.SkillId == scores[0].SkillId).Select(q => q.QuestionId).FirstAsync());

        var untouched = await read.PlacementSkillResults
            .SingleAsync(r => r.PlacementId == placementId && r.SkillId != scores[0].SkillId);
        Assert.Equal(PlacementResultStatus.NotMeasured, untouched.ResultStatus);
        Assert.Equal(PlacementFailureReason.CandidateSkipped, untouched.FailureReason);
    }
}
