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
/// #156: the system must detect when practice accuracy has diverged from the placement band and say
/// so plainly, instead of quietly rewriting the learner's path. One test per rule:
/// <list type="bullet">
/// <item>twenty valid answers are required before any conclusion;</item>
/// <item>sub-three-second answers are excluded from the sample;</item>
/// <item>accuracy under the floor, with a band below, produces a proposal carrying the numbers;</item>
/// <item>"later" changes nothing, "adjust" lowers the path and records the reason;</item>
/// <item>a new learner with no placement gets "not placed", never a guess.</item>
/// </list>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CalibrationServiceTests(DatabaseFixture database)
{
    private const string CertCode = "CAL156";

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (CalibrationService service, LearnerService learners) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        return (new CalibrationService(db, learners), learners);
    }

    /// <summary>A certificate with three bands, a learner placed at B2, an active path from B2, and
    /// a placement result to diverge from.</summary>
    private async Task<(int UserId, int PathId, int SectionId)> SeedPlacedLearnerAsync(string certCode, string placedBand = "B2")
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Calibration {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
              VALUES (@cert, 'READ', N'Reading', 'reading', 1, 10, 90, 10);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
              VALUES (@skill, @cert, 'reading', 'P5', N'Sentences', 40, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 10, 39, 1),
              (@cert, 'B1', N'Intermediate', 40, 59, 2),
              (@cert, 'B2', N'Upper intermediate', 60, 90, 3);

            INSERT INTO Roles (Code, Name) SELECT 'learner', N'Learner'
              WHERE NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{Unique("cal")}@test.local', 0x01, N'Cal Learner', @cert);
            DECLARE @user INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES (@cert, N'Placement {certCode}', 'published', 20, 'placement', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
              VALUES (@user, @exam, @cert, 'graded',
                      DATEADD(DAY, -10, SYSDATETIMEOFFSET()), DATEADD(DAY, -10, DATEADD(HOUR, 1, SYSDATETIMEOFFSET())),
                      DATEADD(DAY, -10, SYSDATETIMEOFFSET()), 70, '{placedBand}', 'AttemptEngine', 1);
            DECLARE @attempt INT = SCOPE_IDENTITY();

            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, ResultBandCode, AttemptId, TotalScore, TakenAt, WrittenByService, ProjectionVersion)
              VALUES (@user, @cert, @exam, 'done', '{placedBand}', @attempt, 70, DATEADD(DAY, -10, SYSDATETIMEOFFSET()), 'AttemptEngine', 1);

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
              VALUES (@cert, 'B1', 'B2', N'Template {certCode}');
            DECLARE @tpl INT = SCOPE_IDENTITY();
            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title, EstimatedMinutes)
              VALUES (@tpl, @skill, @cert, 1, N'Module', 20);
            DECLARE @mod INT = SCOPE_IDENTITY();
            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
              VALUES (@mod, @cert, @tpl, 'practice_section', @sec, 10, 1);
            DECLARE @item INT = SCOPE_IDENTITY();

            INSERT INTO LearnerPaths (UserId, CertId, TemplateId, CurrentBandCode, Status)
              VALUES (@user, @cert, @tpl, '{placedBand}', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps (LearnerPathId, PathModuleItemId, TemplateId, Status)
              VALUES (@path, @item, @tpl, 'available');

            SELECT @user AS UserId, @path AS PathId, @sec AS SectionId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var user = await db.Users.Where(u => u.ActiveCertId == db.Certificates.Single(c => c.Code == certCode).CertId)
            .OrderByDescending(u => u.UserId).FirstAsync();
        var path = await db.LearnerPaths.Where(p => p.UserId == user.UserId).Select(p => p.LearnerPathId).SingleAsync();
        var section = await db.Sections.Where(s => s.CertId == user.ActiveCertId!.Value).Select(s => s.SectionId).SingleAsync();
        return (user.UserId, path, section);
    }

    /// <summary>
    /// Seeds a finished practice session with <paramref name="correct"/> right answers out of
    /// <paramref name="total"/>, each answer taken <paramref name="secondsApart"/>, and
    /// <paramref name="rushed"/> of them taken 1 second after the previous one.
    /// </summary>
    private async Task SeedPracticeAsync(
        int userId, int sectionId, int total, int correct, int secondsApart, int rushed = 0)
    {
        var questionIds = await EnsureQuestionsAsync(sectionId, total);

        await database.SqlAsync($"""
            DECLARE @sec INT = {sectionId};
            DECLARE @cert INT = (SELECT CertId FROM Sections WHERE SectionId = @sec);
            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
              VALUES ({userId}, @cert, @sec, 'reading', {total}, 'finished', {correct},
                      DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()));
            DECLARE @session INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var sessionId = await db.PracticeSessions.Where(s => s.UserId == userId).MaxAsync(s => s.SessionId);

        // A running clock: each answer is `secondsApart` after the one before it, except the rushed
        // tail, which is one second after its predecessor. The first answer of a sitting has no
        // predecessor and so can never be judged rushed — that is a real limit of a per-answer clock.
        var timestamps = new DateTime[total];
        var clock = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < total; i++)
        {
            if (i > 0)
            {
                clock = clock.AddSeconds(i >= total - rushed ? 1 : secondsApart);
            }

            timestamps[i] = clock;
        }

        for (var i = 0; i < total; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = sessionId,
                QuestionId = questionIds[i],
                SectionId = sectionId,
                SelectedOptionId = null,
                ResponseText = "x",
                IsCorrect = i < correct,
                DisplayOrder = i + 1,
                AnsweredAt = timestamps[i],
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<int[]> EnsureQuestionsAsync(int sectionId, int count)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var existing = await db.Questions.Where(q => q.SectionId == sectionId).Select(q => q.QuestionId).ToListAsync();
        if (existing.Count >= count)
        {
            return existing.Take(count).ToArray();
        }

        var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId);
        await database.SqlAsync($"""
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
              SELECT TOP 1 e.ExamId, {section.CertId}, {sectionId}, 99 FROM Exams e WHERE e.CertId = {section.CertId};
            """);

        var groupId = await db.QuestionGroups.Where(g => g.SectionId == sectionId).Select(g => g.GroupId).FirstAsync();
        var examId = await db.Exams.Where(e => e.CertId == section.CertId).Select(e => e.ExamId).FirstAsync();
        for (var i = existing.Count; i < count; i++)
        {
            db.Questions.Add(new Question
            {
                GroupId = groupId,
                ExamId = examId,
                SectionId = sectionId,
                SkillId = section.SkillId,
                Stem = $"Question {i + 1}",
                CefrLevel = CefrLevel.B1,
                QuestionType = QuestionType.Mcq.ToCode(),
                DisplayOrder = 100 + i,
            });
        }

        await db.SaveChangesAsync();
        return await db.Questions.Where(q => q.SectionId == sectionId).OrderBy(q => q.QuestionId)
            .Select(q => q.QuestionId).Take(count).ToArrayAsync();
    }

    [Fact]
    public async Task Below_twenty_valid_answers_says_not_yet_and_proposes_nothing()
    {
        var certCode = Unique("CAL19");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode);
        await SeedPracticeAsync(userId, sectionId, total: 19, correct: 0, secondsApart: 12);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.BelowThreshold, view.Reason);
        Assert.Null(view.Proposal);
        Assert.Equal(19, view.ValidAnswerCount);
        Assert.Equal(CalibrationService.MinimumValidAnswers, view.RequiredValidAnswerCount);
    }

    [Fact]
    public async Task Answers_under_three_seconds_are_not_evidence()
    {
        var certCode = Unique("CALRUSH");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode);
        // 25 answers, all wrong, the last 10 of them one second apart.
        await SeedPracticeAsync(userId, sectionId, total: 25, correct: 0, secondsApart: 12, rushed: 10);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        // All ten one-second answers are excluded: each is judged against the answer before it, and
        // the clock only restarts after a gap long enough to count. Nothing here decides *why* an
        // answer was fast — it simply is not evidence about level.
        Assert.Equal(15, view.ValidAnswerCount);
        Assert.Equal(10, view.ExcludedRushedCount);
        // 15 valid answers is still under the threshold, so there is nothing to conclude yet.
        Assert.Equal(CalibrationReason.BelowThreshold, view.Reason);
        Assert.Null(view.Proposal);
    }

    [Fact]
    public async Task Twenty_wrong_answers_against_a_B2_placement_propose_the_band_below_with_the_numbers()
    {
        var certCode = Unique("CALDIV");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        // 3 correct of 20 = 15%, well under the 40% floor.
        await SeedPracticeAsync(userId, sectionId, total: 20, correct: 3, secondsApart: 12);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.Diverged, view.Reason);
        Assert.NotNull(view.Proposal);
        Assert.Equal("B1", view.Proposal!.ProposedBandCode);
        Assert.Equal(20, view.Proposal.ValidAnswerCount);
        Assert.Equal(15m, view.Proposal.ObservedAccuracyPct);
        // The sentence names the counts and both bands, so the learner is not asked to trust a number.
        Assert.Contains("20", view.Proposal.Evidence, StringComparison.Ordinal);
        Assert.Contains("15", view.Proposal.Evidence, StringComparison.Ordinal);
        Assert.Contains("B2", view.Proposal.Evidence, StringComparison.Ordinal);
        Assert.Contains("B1", view.Proposal.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accuracy_at_or_above_the_floor_is_consistent_and_proposes_nothing()
    {
        var certCode = Unique("CALOK");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        // 9 of 20 = 45%: under the 60% assumption, above the 40% floor. Not a divergence.
        await SeedPracticeAsync(userId, sectionId, total: 20, correct: 9, secondsApart: 12);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.Consistent, view.Reason);
        Assert.Null(view.Proposal);
    }

    [Fact]
    public async Task Deferring_leaves_the_path_alone_and_adjusting_lowers_it_with_the_reason_stored()
    {
        var certCode = Unique("CALDEC");
        var (userId, pathId, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        await SeedPracticeAsync(userId, sectionId, total: 20, correct: 2, secondsApart: 12);
        var (service, _) = CreateServices();

        var deferred = await service.DecideAsync(userId, adjust: false);
        Assert.Equal(CalibrationDecisionKind.Deferred, deferred.Decision);
        Assert.False(deferred.PathAdjusted);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var path = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == pathId);
            Assert.Equal("B2", path.CurrentBandCode);

            var decision = await db.CalibrationDecisions.SingleAsync(d => d.UserId == userId);
            Assert.Equal(CalibrationDecisionKind.Deferred, decision.Decision);
            Assert.Equal("B2", decision.PlacedBandCode);
            Assert.Equal("B1", decision.ProposedBandCode);
            Assert.Equal(20, decision.SampleSize);
        }

        // "Later" is remembered: the same evidence does not raise the proposal again.
        var afterDefer = await service.GetCalibrationAsync(userId);
        Assert.Equal(CalibrationReason.Deferred, afterDefer.Reason);
        Assert.Null(afterDefer.Proposal);

        // A fresh decision record, because the deferred row is a closed answer.
        await database.SqlAsync($"""
            DELETE FROM CalibrationDecisions WHERE UserId = {userId};
            """);

        var adjusted = await service.DecideAsync(userId, adjust: true);
        Assert.Equal(CalibrationDecisionKind.Adjusted, adjusted.Decision);
        Assert.True(adjusted.PathAdjusted);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var path = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == pathId);
            Assert.Equal("B1", path.CurrentBandCode);

            var decision = await db.CalibrationDecisions.SingleAsync(d => d.UserId == userId);
            Assert.Equal(CalibrationDecisionKind.Adjusted, decision.Decision);
            Assert.Equal("B1", decision.ProposedBandCode);
            Assert.Equal(10m, decision.ObservedAccuracyPct);
        }
    }

    [Fact]
    public async Task Accepting_the_proposal_is_not_proposed_again()
    {
        var certCode = Unique("CALAGAIN");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        await SeedPracticeAsync(userId, sectionId, total: 20, correct: 2, secondsApart: 12);
        var (service, _) = CreateServices();

        Assert.Equal(CalibrationReason.Diverged, (await service.GetCalibrationAsync(userId)).Reason);

        await service.DecideAsync(userId, adjust: true);

        // More bad practice, on the band the learner just moved down to. There is no lower band the
        // evidence justifies here — the path already starts at B1 — so the proposal must not repeat.
        await SeedPracticeAsync(userId, sectionId, total: 20, correct: 1, secondsApart: 14);
        var after = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.Calibrated, after.Reason);
        Assert.Null(after.Proposal);
    }

    [Fact]
    public async Task A_learner_with_no_placement_is_told_it_is_unplaced_rather_than_guessed_at()
    {
        var certCode = Unique("CALNEW");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        // Remove the placement: a new learner who has only ever practised.
        await database.SqlAsync($"""
            DELETE FROM PlacementTests WHERE UserId = {userId};
            DELETE FROM ExamAttempts WHERE UserId = {userId};
            """);
        await SeedPracticeAsync(userId, sectionId, total: 25, correct: 0, secondsApart: 12);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.NotPlaced, view.Reason);
        Assert.Null(view.Proposal);
    }

    [Fact]
    public async Task Practice_from_before_the_placement_is_not_evidence_about_it()
    {
        var certCode = Unique("CALOLD");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode, placedBand: "B2");
        await SeedPracticeAsync(userId, sectionId, total: 25, correct: 0, secondsApart: 12);
        // Push the practice before the placement sitting, which happened ten days ago.
        await database.SqlAsync($"""
            UPDATE PracticeSessions SET StartedAt = DATEADD(DAY, -30, SYSDATETIMEOFFSET())
              WHERE UserId = {userId};
            UPDATE PracticeAnswers SET AnsweredAt = DATEADD(DAY, -30, AnsweredAt)
              WHERE SessionId IN (SELECT SessionId FROM PracticeSessions WHERE UserId = {userId});
            """);
        var (service, _) = CreateServices();

        var view = await service.GetCalibrationAsync(userId);

        Assert.Equal(CalibrationReason.BelowThreshold, view.Reason);
        Assert.Equal(0, view.ValidAnswerCount);
        Assert.Null(view.Proposal);
    }

    [Fact]
    public async Task An_answer_written_by_the_practice_drill_carries_the_time_it_was_written()
    {
        var certCode = Unique("CALSTAMP");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode);
        var questionId = (await EnsureQuestionsAsync(sectionId, 1))[0];

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var practice = new PracticeService(db, learners);
        // The drill only serves questions with options; the fixture's are bare mcq, so give this
        // one a keyed option to be drillable.
        await database.SqlAsync($"""
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
              VALUES ({questionId}, 'mcq', 'A', N'Option A', 1);
            """);
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));

        var before = DateTime.UtcNow.AddSeconds(-1);
        await practice.SubmitAnswerAsync(userId, session.SessionId, questionId, new SubmitPracticeAnswerInput { SelectedOptionId = null });
        var after = DateTime.UtcNow.AddSeconds(1);

        var answer = await db.PracticeAnswers.AsNoTracking()
            .SingleAsync(a => a.SessionId == session.SessionId && a.QuestionId == questionId);
        Assert.NotNull(answer.AnsweredAt);
        Assert.InRange(answer.AnsweredAt!.Value, before, after);
    }

    [Fact]
    public async Task A_learner_who_improves_stops_being_told_they_diverge()
    {
        // Found by review: the evidence query counted every valid answer since the placement with no
        // window, while EvidenceText promised "on your last {count} practice answers". One bad week
        // then anchored the percentage forever - the learner improved, the page kept saying otherwise,
        // and because the proposal is recomputed on every load it was re-proposed indefinitely.
        var certCode = Unique("CALW");
        var (userId, _, sectionId) = await SeedPlacedLearnerAsync(certCode);

        // 20 wrong first, then 25 right - a learner who fixed exactly what the proposal described.
        await SeedPracticeAsync(userId, sectionId, 20, 0, secondsApart: 30);
        await SeedPracticeAsync(userId, sectionId, 25, 25, secondsApart: 30);

        var (service, _) = CreateServices();
        var view = await service.GetCalibrationAsync(userId);

        // The last 20 answers are all correct, so there is nothing to calibrate against any more.
        Assert.Equal(20, view.ValidAnswerCount);
        Assert.NotEqual(CalibrationReason.Diverged, view.Reason);
    }
}
