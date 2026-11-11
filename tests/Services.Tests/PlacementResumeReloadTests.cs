using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #216 / PLC-13, PLC-14, PLC-15, PLC-16: resume after exit, reload without advancing, the one
/// recording retry, and a reload during grading that writes nothing.
/// <para>
/// <b>What would pass these tests while being wrong.</b> "Reload restores the item" is asserted by
/// the ITEM ID and by the absence of a write, never by a 200: a page that advanced and answered 200
/// would pass a status-code test. "Does not advance" is asserted on the attempt's own timestamps,
/// because a re-saved draft advances nothing visible while moving the sitting's clock. "Exactly one
/// retry" is asserted on the THIRD interruption, because a counter that resets would still satisfy a
/// test that only checks the second one.
/// </para>
/// <para>Isolation: its own database, created and dropped in <c>finally</c>.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementResumeReloadTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE216Resume_{Guid.NewGuid():N}"[..26];
    private string _connectionString = null!;

    public PlacementResumeReloadTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private PlacementService Service(CompanionDbContext db) => new(db, new LearnerService(db));

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>Two objective skills and one productive skill, so "which skills are done" has three
    /// different answers to choose between and one that is a recording.
    /// <para>
    /// <b>#269: NOT migrated to a pool-only paper, and the reason is production code.</b> The recording
    /// retry reads its response with <c>FreeResponses.QuestionId == questionId</c>
    /// (AttemptEngine.PlacementResume.cs:201); a pool response names <c>ExamItemId</c> instead, so the
    /// lookup misses and throws "Free-response question N not found in attempt". The paper keeps its
    /// legacy question rows, and its audio lives on the group, until that reader resolves the roster.
    /// </para>
    /// </summary>
    private sealed record Fixture(int CertId, int ExamId, int ListeningSkillId, int ReadingSkillId,
        int SpeakingSkillId, int Q1, int Q2, int Q3, int CorrectOption);

    private async Task<Fixture> SeedAsync()
    {
        var code = Unique("RS216");
        await using var db = Db();

        db.Certificates.Add(new Certificate { Code = code, Name = $"Cert {code}", IsActive = true });
        await db.SaveChangesAsync();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();

        db.Skills.AddRange(
            new Skill { CertId = certId, Code = "LIST", Name = "Listening", Modality = "listening", DisplayOrder = 1, DurationMinutes = 20, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 2, DurationMinutes = 20, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 },
            new Skill { CertId = certId, Code = "SPEAK", Name = "Speaking", Modality = "speaking", DisplayOrder = 3, DurationMinutes = 20, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1 });
        await db.SaveChangesAsync();

        var listSkill = await db.Skills.Where(s => s.CertId == certId && s.Code == "LIST").Select(s => s.SkillId).SingleAsync();
        var readSkill = await db.Skills.Where(s => s.CertId == certId && s.Code == "READ").Select(s => s.SkillId).SingleAsync();
        var speakSkill = await db.Skills.Where(s => s.CertId == certId && s.Code == "SPEAK").Select(s => s.SkillId).SingleAsync();

        db.Sections.AddRange(
            new Section { SkillId = listSkill, CertId = certId, Modality = "listening", Code = "L1", Name = "Listening", ExpectedQuestionCount = 1, DisplayOrder = 1 },
            new Section { SkillId = readSkill, CertId = certId, Modality = "reading", Code = "R1", Name = "Reading", ExpectedQuestionCount = 1, DisplayOrder = 2 },
            new Section { SkillId = speakSkill, CertId = certId, Modality = "speaking", Code = "S1", Name = "Speaking", ExpectedQuestionCount = 1, DisplayOrder = 3 });
        await db.SaveChangesAsync();

        db.Exams.Add(new Exam
        {
            CertId = certId,
            Name = "Placement resume 216",
            Status = "published",
            DurationMinutes = 60,
            Purpose = "placement",
            QuestionCount = 3,
        });
        await db.SaveChangesAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();

        var questionIds = new int[3];
        for (var index = 0; index < 3; index++)
        {
            var sectionCode = index switch { 0 => "L1", 1 => "R1", _ => "S1" };
            var section = await db.Sections.Where(s => s.Code == sectionCode && s.CertId == certId).SingleAsync();
            var group = new QuestionGroup { ExamId = examId, CertId = certId, SectionId = section.SectionId, DisplayOrder = index + 1 };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync();

            var question = new Question
            {
                GroupId = group.GroupId,
                ExamId = examId,
                SectionId = section.SectionId,
                SkillId = section.SkillId,
                Stem = $"Q{index + 1}",
                CefrLevel = CefrLevel.B1,
                QuestionType = sectionCode == "S1" ? "free_response" : "mcq",
                DisplayOrder = index + 1,
                ResponseSeconds = sectionCode == "S1" ? 60 : null,
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            questionIds[index] = question.QuestionId;

            if (sectionCode != "S1")
            {
                db.QuestionOptions.AddRange(
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "A", Text = "Right", IsCorrect = true },
                    new QuestionOption { QuestionId = question.QuestionId, QuestionType = "mcq", Label = "B", Text = "Wrong", IsCorrect = false });
            }
        }

        db.ScoreBands.Add(new ScoreBand { CertId = certId, Code = "B1", Name = "Intermediate", MinTotal = 0, MaxTotal = 200, DisplayOrder = 1 });
        await db.SaveChangesAsync();

        var correct = await db.QuestionOptions
            .Where(o => o.QuestionId == questionIds[0] && o.IsCorrect)
            .Select(o => o.OptionId).SingleAsync();

        return new Fixture(certId, examId, listSkill, readSkill, speakSkill, questionIds[0], questionIds[1], questionIds[2], correct);
    }

    private async Task<int> SeedLearnerAsync(int certId)
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
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Start a placement and pass its preflight. The fixture carries audio and a speaking section, so
    /// the gate is real - a test that skipped it would be testing a path no learner takes.
    /// </summary>
    private async Task<PlacementTestInfo> StartAsync(int userId, int examId)
    {
        PlacementTestInfo info;
        await using (var db = Db())
        {
            info = await Service(db).StartPlacementTestAsync(userId, examId);
        }

        await using (var db = Db())
        {
            await new AttemptEngine(db).VerifyPreflightAsync(info.AttemptId, userId);
        }

        return info;
    }

    /// <summary>Advance to the speaking stage, where the recording task lives.</summary>
    private async Task<int> AdvanceToSpeakingAsync(int userId, int placementId)
    {
        await using var db = Db();
        var attemptId = await db.PlacementTests
            .Where(p => p.PlacementId == placementId)
            .Select(p => p.AttemptId!.Value)
            .SingleAsync();
        var engine = new AttemptEngine(db);
        await engine.TransitionToNextSkillAsync(attemptId, userId);
        await engine.TransitionToNextSkillAsync(attemptId, userId);
        return attemptId;
    }

    // ------------------------------------------------------------------------------------------
    // PLC-14: returning after an exit
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The resume screen names the start date, which skills are done, the active stage and the count
    /// of synced answers - and it offers both choices.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> counting the client's queued payloads. The
    /// server cannot see the browser's queue, so the only honest number is
    /// <c>AttemptAnswers</c>/<c>FreeResponses</c> rows. This test writes two answers and asserts the
    /// count is 2, then asserts the count is still 2 after a resume - a screen that reported its own
    /// unsaved typing would say 3.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Returning_after_an_exit_names_the_start_date_done_skills_active_stage_and_synced_count()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var info = await StartAsync(userId, fixture.ExamId);
        var placementId = info.PlacementId;
        await using (var db = Db())
        {
            await Service(db).SaveAnswerAsync(userId, placementId, fixture.Q1, fixture.CorrectOption, isFlagged: false);
        }

        await using (var db = Db())
        {
            var resume = await Service(db).GetResumeAsync(userId, placementId);

            Assert.Equal(placementId, resume.PlacementId);
            Assert.Equal("Placement resume 216", resume.ExamName);
            Assert.True(resume.StartedAt > DateTimeOffset.MinValue);
            Assert.Equal(3, resume.TotalStages);
            Assert.Equal(1, resume.ActiveStage);
            Assert.Equal("Listening", resume.ActiveSkillName);

            // Two objective rows exist from attempt creation (Listening and Reading); only Listening
            // carries a CHOICE. The free response has no draft, so it is not answered either.
            Assert.Equal(1, resume.SyncedAnswerCount);
            Assert.Equal(3, resume.TotalQuestions);

            var listening = resume.Skills.Single(s => s.Code == "LIST");
            Assert.True(listening.IsActive);
            Assert.False(listening.IsDone);
            Assert.Equal(1, listening.AnsweredCount);
            Assert.Equal(1, listening.TotalQuestions);

            // The skills the sitting has not reached are not "done": a placement that reported them
            // done would offer a resume into a part that never opened.
            Assert.False(resume.Skills.Single(s => s.Code == "READ").IsDone);
            Assert.False(resume.Skills.Single(s => s.Code == "SPEAK").IsDone);
        }

        // A second read is the same answer: nothing was stamped by asking.
        await using (var db = Db())
        {
            var again = await Service(db).GetResumeAsync(userId, placementId);
            Assert.Equal(1, again.SyncedAnswerCount);
        }
    }

    /// <summary>
    /// PLC-14's other half: the active stage moves as the sitting advances, so "which skills are done"
    /// is not a constant.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> marking every skill before the active one done
    /// only when all its items are answered. A placement closes a skill when the sitting moves past
    /// it, and a partly answered closed skill must read done or the screen offers a resume into a
    /// locked part.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_skill_the_sitting_moved_past_reads_done_even_when_some_of_its_items_are_unanswered()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;

        // Advance to Reading through the engine, exactly as the sitting does.
        await using (var db = Db())
        {
            var attemptId = await db.PlacementTests.Where(p => p.PlacementId == placementId).Select(p => p.AttemptId!.Value).SingleAsync();
            await new AttemptEngine(db).TransitionToNextSkillAsync(attemptId, userId);
        }

        await using (var db = Db())
        {
            var resume = await Service(db).GetResumeAsync(userId, placementId);

            Assert.True(resume.Skills.Single(s => s.Code == "LIST").IsDone);
            Assert.True(resume.Skills.Single(s => s.Code == "READ").IsActive);
            Assert.Equal(2, resume.ActiveStage);
            Assert.False(resume.Skills.Single(s => s.Code == "SPEAK").IsDone);
            // Listening was never answered at all, and it is still done: done is about closure.
            Assert.Equal(0, resume.Skills.Single(s => s.Code == "LIST").AnsweredCount);
        }
    }

    // ------------------------------------------------------------------------------------------
    // PLC-14: explicit abandon
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Abandon is a lifecycle act on the rows that already exist, and it refuses a finished sitting.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> setting only <c>PlacementTests.Status</c>.
    /// The linked attempt would stay in progress and keep appearing on the dashboard's "unfinished"
    /// list forever. Both rows are asserted, and the second abandon is a no-op rather than a throw.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Explicit_abandon_closes_the_placement_and_its_attempt_and_is_idempotent()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;

        await using (var db = Db())
        {
            await Service(db).AbandonAsync(userId, placementId);
        }

        await using (var db = Db())
        {
            var placement = await db.PlacementTests.SingleAsync(p => p.PlacementId == placementId);
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == placement.AttemptId);
            Assert.Equal(PlacementStatus.Abandoned.ToCode(), placement.Status);
            Assert.Equal(AttemptStatus.Abandoned.ToCode(), attempt.Status);

            // Idempotent: a second click is not an error.
            await Service(db).AbandonAsync(userId, placementId);
        }

        // A resume of an abandoned sitting is refused, not silently resumed.
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<PlacementException>(() => Service(db).GetResumeAsync(userId, placementId));
            Assert.Equal(PlacementException.PlacementNotActive, error.Code);
        }
    }

    // ------------------------------------------------------------------------------------------
    // PLC-13: inactivity expiry
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The deadline is the shorter of the two windows, and a write moves the inactivity arm while a
    /// read does not.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> asserting only that a placement's expiry is 48
    /// hours after creation. That is true of the first minute and false forever after: the rule is
    /// re-derived on every write. So this test writes an answer, asserts the deadline moved to
    /// <c>now + 48 h</c> clamped by <c>started + 72 h</c>, then reads twice and asserts nothing moved.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_deadline_is_the_shorter_window_and_only_a_write_moves_the_inactivity_arm()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        int placementId;
        int attemptId;
        var started = await StartAsync(userId, fixture.ExamId);
        placementId = started.PlacementId;
        attemptId = started.AttemptId;

        await using (var db = Db())
        {
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            // Nothing written, so the inactivity arm is measured from the start: 48 h, not 72.
            Assert.Equal(PlacementSitting.Inactivity, attempt.ExpiresAt - attempt.StartedAt);
            Assert.Null(attempt.LastActivityAt);
        }

        // Move the start back 10 hours, so the stale 48-hour deadline (start + 48 = now + 38 h) can be
        // distinguished from a fresh one (now + 48 h). The attempt is still open, which is the point:
        // a write is what keeps it open.
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE ExamAttempts
                SET StartedAt = DATEADD(hour, -10, SYSDATETIMEOFFSET()),
                    ExpiresAt = DATEADD(hour, 38, SYSDATETIMEOFFSET())
                WHERE AttemptId = {attemptId}
                """);
        }

        await using (var db = Db())
        {
            await Service(db).SaveAnswerAsync(userId, placementId, fixture.Q1, fixture.CorrectOption, isFlagged: false);
        }

        await using (var db = Db())
        {
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            Assert.NotNull(attempt.LastActivityAt);
            // The inactivity arm won, and it was re-derived rather than frozen at create time: the
            // deadline is now+48h, not the start+48h the row carried before the write.
            Assert.Equal(PlacementSitting.Inactivity, attempt.ExpiresAt - attempt.LastActivityAt.Value);
            Assert.True(attempt.ExpiresAt > attempt.StartedAt.AddHours(38));
            Assert.True(attempt.ExpiresAt < attempt.StartedAt + PlacementSitting.MaxLifetime);
        }

        // Now the other arm: a sitting started 70 hours ago cannot be kept alive by a write, because
        // 70 + 48 is past the 72-hour cap.
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE ExamAttempts
                SET StartedAt = DATEADD(hour, -70, SYSDATETIMEOFFSET()),
                    LastActivityAt = DATEADD(hour, -30, SYSDATETIMEOFFSET()),
                    ExpiresAt = DATEADD(hour, 2, SYSDATETIMEOFFSET())
                WHERE AttemptId = {attemptId}
                """);
        }

        await using (var db = Db())
        {
            await Service(db).SaveAnswerAsync(userId, placementId, fixture.Q1, fixture.CorrectOption, isFlagged: true);
        }

        await using (var db = Db())
        {
            var attempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attemptId);
            Assert.Equal(PlacementSitting.MaxLifetime, attempt.ExpiresAt - attempt.StartedAt);
        }

        // A read moves neither timestamp - and neither does an EMPTY sync, which is what a keepalive
        // or a poller sends. That is the write-shaped read that would otherwise keep a dead sitting
        // alive forever, so it is part of the set.
        var before = await ReadClockAsync(attemptId);
        await using (var db = Db())
        {
            await Service(db).GetResumeAsync(userId, placementId);
            await Service(db).GetResumeAsync(userId, placementId);
            await Service(db).GetPlacementTakeAsync(userId, placementId, null);
            await new AttemptEngine(db).SaveSittingSyncAsync(attemptId, new SittingSyncRequest(), userId);
            await new AttemptEngine(db).SaveSittingSyncAsync(attemptId, new SittingSyncRequest(), userId);
        }
        var after = await ReadClockAsync(attemptId);
        Assert.Equal(before, after);
    }

    private async Task<(DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastActivityAt)> ReadClockAsync(int attemptId)
    {
        await using var db = Db();
        var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        return (attempt.StartedAt, attempt.ExpiresAt, attempt.LastActivityAt);
    }

    /// <summary>
    /// A mock keeps its own absolute deadline: the inactivity rule must not leak onto the exam path.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> applying the rule engine-wide. Every 75-minute
    /// mock would then get a 48-hour deadline the first time an answer was saved, and no placement
    /// test would notice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_mock_exam_keeps_its_own_deadline_and_gets_no_activity_stamp()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        await using (var db = Db())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE Exams SET Purpose = 'mock', DurationMinutes = 75 WHERE ExamId = {fixture.ExamId}");
        }

        int attemptId;
        await using (var db = Db())
        {
            var engine = new AttemptEngine(db);
            attemptId = (await engine.CreateAttemptAsync(userId, fixture.CertId, fixture.ExamId, 75)).AttemptId;
            await engine.VerifyPreflightAsync(attemptId, userId);
        }

        var before = await ReadClockAsync(attemptId);

        await using (var db = Db())
        {
            await new AttemptEngine(db).SaveAnswerAsync(attemptId, userId, fixture.Q1, fixture.CorrectOption, isFlagged: false);
        }

        var after = await ReadClockAsync(attemptId);
        Assert.Equal(TimeSpan.FromMinutes(75), after.ExpiresAt - after.StartedAt);
        Assert.Null(after.LastActivityAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
    }

    // ------------------------------------------------------------------------------------------
    // PLC-14: a reload restores the exact item and does not advance
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The heart of PLC-14. A reload is <see cref="PlacementService.GetPlacementTakeAsync"/> with no
    /// <c>q</c>, and it must land on the item the learner was on.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> asserting a 200. A page that advanced one
    /// question and answered 200 would pass that. So this asserts three separate facts: the CURRENT
    /// item's id equals the one the learner left, no answer row was created or changed by the reload,
    /// and the attempt's own timestamps did not move - because a reload that re-saved a draft with a
    /// later timestamp advances nothing visible while moving the sitting's clock.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reload_restores_the_exact_item_and_writes_neither_an_answer_nor_a_timestamp()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);
        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;

        // Answer Q1 and move to Q2 through the server, exactly as the sitting does.
        await using (var db = Db())
        {
            var service = Service(db);
            await service.SaveAnswerAsync(userId, placementId, fixture.Q1, fixture.CorrectOption, isFlagged: false);
            var takeToQ2 = await service.GetPlacementTakeAsync(userId, placementId, 2);
            Assert.Equal(fixture.Q2, takeToQ2.CurrentQuestion.QuestionId);

            // Land the position on Q2 the way an answer moves it.
            await service.SaveAnswerAsync(userId, placementId, fixture.Q2, await CorrectOptionAsync(fixture.Q2), isFlagged: true);
        }

        var before = await SnapshotAsync(placementId);

        // The reload: read the sitting with no question asked for. Twice, because a second reload is
        // the case a bug shows up in.
        int reloadedQuestionId;
        await using (var db = Db())
        {
            var reload = await Service(db).GetPlacementTakeAsync(userId, placementId, null);
            reloadedQuestionId = reload.CurrentQuestion.QuestionId;
            await Service(db).GetPlacementTakeAsync(userId, placementId, null);
        }

        // 1. The item is the one the learner was on.
        Assert.Equal(fixture.Q2, reloadedQuestionId);

        // 2. No answer row was written by either read.
        var after = await SnapshotAsync(placementId);
        Assert.Equal(before.AnswerCount, after.AnswerCount);
        Assert.Equal(before.TotalSelected, after.TotalSelected);
        Assert.Equal(before.DraftCount, after.DraftCount);

        // 3. Nothing on the attempt moved. The position and both clocks are identical, which is the
        //    half a status-code test can never see.
        Assert.Equal(before.LastQuestionId, after.LastQuestionId);
        Assert.Equal(before.StartedAt, after.StartedAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.LastActivityAt, after.LastActivityAt);
        Assert.Equal(before.LastAudioOffsetMs, after.LastAudioOffsetMs);
    }

    private async Task<int> CorrectOptionAsync(int questionId)
    {
        await using var db = Db();
        return await db.QuestionOptions.Where(o => o.QuestionId == questionId && o.IsCorrect).Select(o => o.OptionId).SingleAsync();
    }

    private sealed record AttemptSnapshot(
        int AnswerCount, int TotalSelected, int DraftCount,
        int? LastQuestionId, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt,
        DateTimeOffset? LastActivityAt, int LastAudioOffsetMs);

    private async Task<AttemptSnapshot> SnapshotAsync(int placementId)
    {
        await using var db = Db();
        var attemptId = await db.PlacementTests.Where(p => p.PlacementId == placementId).Select(p => p.AttemptId!.Value).SingleAsync();
        var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        var answers = await db.AttemptAnswers.AsNoTracking().Where(a => a.AttemptId == attemptId).ToListAsync();
        return new AttemptSnapshot(
            answers.Count,
            answers.Count(a => a.SelectedOptionId != null || a.SelectedOptionIdsJson != null),
            await db.FreeResponses.CountAsync(r => r.AttemptId == attemptId && r.ResponseText != null && r.ResponseText != ""),
            attempt.CurrentExamItemId,
            attempt.StartedAt,
            attempt.ExpiresAt,
            attempt.LastActivityAt,
            attempt.LastAudioOffsetMs);
    }

    // ------------------------------------------------------------------------------------------
    // PLC-15: exactly one recording retry
    // ------------------------------------------------------------------------------------------
    /// <summary>
    /// <b>The input that would pass a weaker test:</b> counting interruptions per page load. This test
    /// sends the SAME session token three times and asserts the count stays at one - a counter that
    /// reset or incremented on each request would fail it.
    /// </summary>
    [Fact]
    public async Task Reloading_the_same_interruption_does_not_grant_a_second_retry()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;
        // Reach the speaking stage, where the recording task lives.
        await AdvanceToSpeakingAsync(userId, placementId);

        PlacementRecordingDecision first, reload, reloadAgain;
        await using (var db = Db())
        {
            var service = Service(db);
            first = await service.RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-A", capturedMs: 4000);
        }
        await using (var db = Db())
        {
            reload = await Service(db).RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-A", capturedMs: 4000);
        }
        await using (var db = Db())
        {
            reloadAgain = await Service(db).RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-A", capturedMs: 4000);
        }

        Assert.Equal(PlacementRecordingOutcome.Retry, first.Outcome);
        Assert.Equal(1, first.Interruptions);
        Assert.Equal(1, reload.Interruptions);
        Assert.Equal(1, reloadAgain.Interruptions);

        await using (var db = Db())
        {
            var row = await db.FreeResponses.SingleAsync(r => r.QuestionId == fixture.Q3);
            Assert.Equal(1, row.RecordingInterruptions);
        }
    }

    /// <summary>
    /// A second, DIFFERENT interruption is the branch that ends retries: under 15 s captured and an
    /// unseen sibling exists, the task is replaced; at or above 15 s the capture is finalised.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> a rule that always retries. Both arms are
    /// asserted in one test, because a rule that finalises everything and a rule that replaces
    /// everything are both "not a retry" and only one of them is right for each input.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_interruption_finalises_above_the_threshold_or_replaces_below_it_and_never_retries_again()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;
        await AdvanceToSpeakingAsync(userId, placementId);

        // The seed has one speaking task, and it is the only question at its band - so there is no
        // unseen sibling and the under-threshold arm must finalise rather than invent a task.
        PlacementRecordingDecision first, second, third;
        await using (var db = Db())
        {
            first = await Service(db).RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-1", capturedMs: 3000);
        }
        await using (var db = Db())
        {
            second = await Service(db).RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-2", capturedMs: 3000);
        }
        await using (var db = Db())
        {
            // A THIRD, distinct interruption. The cap already stands at 2, so this must not become a
            // new retry - the ticket's "no further retries".
            third = await Service(db).RecordRecordingInterruptionAsync(userId, placementId, fixture.Q3, "session-3", capturedMs: 3000);
        }

        Assert.Equal(PlacementRecordingOutcome.Retry, first.Outcome);
        Assert.Equal(PlacementRecordingOutcome.Finalise, second.Outcome);
        Assert.Equal(2, second.Interruptions);
        Assert.NotEqual(PlacementRecordingOutcome.Retry, third.Outcome);

        await using (var db = Db())
        {
            var row = await db.FreeResponses.SingleAsync(r => r.QuestionId == fixture.Q3);
            Assert.Equal(2, row.RecordingInterruptions);
        }
    }

    /// <summary>
    /// The pure rule's three arms, including the sibling branch the seeded paper cannot reach.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> passing a sibling id and expecting a replacement
    /// even when the capture is long enough. PLC-15 says finalise if 15 s were captured - the threshold
    /// outranks the sibling.
    /// </para>
    /// </summary>
    [Fact]
    public void The_recording_rule_retries_once_then_finalises_or_replaces_by_the_captured_duration()
    {
        Assert.Equal(PlacementRecordingOutcome.Retry,
            PlacementRecordingRule.Decide(interruptionsBefore: 0, capturedMs: 0, siblingQuestionId: 77).Outcome);

        Assert.Equal(PlacementRecordingOutcome.Finalise,
            PlacementRecordingRule.Decide(interruptionsBefore: 1, capturedMs: PlacementRecordingRule.FinaliseThresholdMs, siblingQuestionId: 77).Outcome);

        var replace = PlacementRecordingRule.Decide(interruptionsBefore: 1, capturedMs: 14_999, siblingQuestionId: 77);
        Assert.Equal(PlacementRecordingOutcome.ReplaceWithSibling, replace.Outcome);
        Assert.Equal(77, replace.ReplacementQuestionId);

        // One millisecond under 15 s is below, so the boundary is inclusive on the threshold itself.
        Assert.Equal(PlacementRecordingOutcome.Finalise,
            PlacementRecordingRule.Decide(interruptionsBefore: 1, capturedMs: 15_000, siblingQuestionId: 77).Outcome);

        // No sibling and too short to finalise: finalise anyway, never loop.
        Assert.Equal(PlacementRecordingOutcome.Finalise,
            PlacementRecordingRule.Decide(interruptionsBefore: 1, capturedMs: 0, siblingQuestionId: null).Outcome);
    }

    // ------------------------------------------------------------------------------------------
    // PLC-16: a reload during grading is a pure read
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Submitting a placement carrying a free response queues one <c>AiGrading</c> and leaves the
    /// attempt <c>submitted</c>. Reading the resume state, the result and the grading status must not
    /// enqueue a second job, not reset the task's status, and not move a score column.
    /// <para>
    /// <b>The input that would pass a weaker test:</b> asserting a 200 from the result page. This
    /// test counts <c>AiGradings</c> rows and compares the attempt's score columns before and after,
    /// which is the fact #251 made one writer's.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reload_during_grading_writes_no_grading_row_and_moves_no_score()
    {
        var fixture = await SeedAsync();
        var userId = await SeedLearnerAsync(fixture.CertId);

        var placementId = (await StartAsync(userId, fixture.ExamId)).PlacementId;
        var attemptId = await AdvanceToSpeakingAsync(userId, placementId);
        await using (var db = Db())
        {
            // A draft, so submit has something to queue.
            await new AttemptEngine(db).SaveDraftAsync(attemptId, userId, fixture.Q3, "My spoken answer, transcribed.");
        }

        await using (var db = Db())
        {
            await Service(db).SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
        }

        int attemptIdFinal;
        int gradingsBefore;
        string statusBefore;
        int? scoreBefore;
        string? bandBefore;
        int versionBefore;

        await using (var db = Db())
        {
            attemptIdFinal = await db.PlacementTests.Where(p => p.PlacementId == placementId).Select(p => p.AttemptId!.Value).SingleAsync();
            var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptIdFinal);
            gradingsBefore = await db.AiGradings.CountAsync(g => g.FreeResponse.AttemptId == attemptIdFinal);
            statusBefore = attempt.Status;
            scoreBefore = attempt.TotalScore;
            bandBefore = attempt.BandCode;
            versionBefore = attempt.ProjectionVersion;
            Assert.Equal(1, gradingsBefore);
        }

        // Every read a reload performs. #217 / PLC-34 changed what the result read answers: a placement
        // whose productive task is still queued is GRADING, so the result page renders the objective
        // half now and fills in the productive half as each task returns. It used to throw
        // PlacementNotFinished and bounce the learner back to a sitting they had already submitted,
        // which is exactly the progressive-result failure criterion 5 names. What this test still holds
        // - and the reason it exists - is that ALL of these reads write nothing.
        await using (var db = Db())
        {
            await Assert.ThrowsAsync<PlacementException>(() => Service(db).GetResumeAsync(userId, placementId));
        }
        await using (var db = Db())
        {
            var interim = await Service(db).GetPlacementResultAsync(userId, placementId);
            Assert.False(interim.IsGraded);          // the productive half is still in the queue
            Assert.Contains(interim.SkillResults!, r => r.Status == PlacementResultStatus.Grading);
            await Service(db).GetGradingStatusAsync(userId, placementId);
            await Service(db).GetPlacementResultAsync(userId, placementId);
            await Service(db).GetGradingStatusAsync(userId, placementId);
        }

        await using (var db = Db())
        {
            var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptIdFinal);
            Assert.Equal(gradingsBefore, await db.AiGradings.CountAsync(g => g.FreeResponse.AttemptId == attemptIdFinal));
            Assert.Equal(statusBefore, attempt.Status);
            Assert.Equal(scoreBefore, attempt.TotalScore);
            Assert.Equal(bandBefore, attempt.BandCode);
            Assert.Equal(versionBefore, attempt.ProjectionVersion);
        }
    }
}
