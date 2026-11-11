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

            -- #232: the step needs a topic coordinate.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail');

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

            -- #212: the placement is created in progress, its per-skill row is written, and only then is
            -- it marked done. The completion guard reads PlacementSkillResults, so a done INSERT would
            -- run before any row exists and be refused for the wrong reason.
            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, AttemptId, TakenAt)
              VALUES (@user, @cert, @exam, 'in_progress', @attempt, DATEADD(DAY, -10, SYSDATETIMEOFFSET()));
            DECLARE @plc INT = SCOPE_IDENTITY();
            DECLARE @measured DATETIMEOFFSET = DATEADD(DAY, -10, SYSDATETIMEOFFSET());
            INSERT INTO PlacementSkillResults
              (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
               MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
              VALUES (@plc, @skill, @cert, 'measured', 'B2', 50, 4.50,
                      @measured, DATEADD(DAY, 90, @measured), 'AttemptEngine', 1);
            UPDATE PlacementTests
              SET Status = 'done', ResultBandCode = '{placedBand}', TotalScore = 70,
                  WrittenByService = 'AttemptEngine', ProjectionVersion = 1
            WHERE PlacementId = @plc;

            -- #232: a path is a coordinate set. The fixture names the skill's own topic and level rather
            -- than pointing at a template's module item.
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
              VALUES (@user, @cert, '{placedBand}', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier, Status)
              VALUES (@path, @cert, @skill, 'reading',
                      (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading'),
                      'B1', 'focus_practice', 'developing', 'available');
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
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var section = await db.Sections.AsNoTracking()
            .Where(s => s.SectionId == sectionId)
            .Select(s => new { s.CertId, s.Modality })
            .SingleAsync();

        // #269: the answers are pool answers - the shape a section drill now writes (#263). The cell
        // comes from the item, and a section drill's session names its section, so the paper the items
        // are bound into is the section's own.
        var bank = await EnsurePoolPositionsAsync(section.CertId, sectionId, section.Modality, total);

        await database.SqlAsync($"""
            DECLARE @sec INT = {sectionId};
            DECLARE @cert INT = (SELECT CertId FROM Sections WHERE SectionId = @sec);
            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
              VALUES ({userId}, @cert, @sec, '{section.Modality}', {total}, 'finished', {correct},
                      DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()));
            """);

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
                QuestionId = null,
                SectionId = null,
                ItemId = bank.ItemIds[i],
                ItemVersionId = bank.VersionIds[i],
                SelectedLabel = null,
                SelectedOptionId = null,
                ResponseText = "x",
                IsCorrect = i < correct,
                DisplayOrder = i + 1,
                AnsweredAt = timestamps[i],
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// #269: the drill bank is the pool. A placement certificate with a section, and a paper that binds
    /// <paramref name="count"/> four-option items into it, so a section drill draws pool stock.
    /// <para>
    /// The items are <c>B1</c> on a topic <c>PoolFixture</c> provisions, and the paper is
    /// <paramref name="purpose"/>-purpose so a placement fixture and a drill fixture do not collide on
    /// <c>UX_Exams_Cert_Name</c>.
    /// </para>
    /// </summary>
    private async Task<(int ExamId, int[] ItemIds, int[] VersionIds, int[] ExamItemIds)> EnsurePoolPositionsAsync(
        int certId, int sectionId, string sectionModality, int count, string purpose = "mock")
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var examId = await db.Exams
            .Where(e => e.CertId == certId && e.Purpose == purpose)
            .Select(e => (int?)e.ExamId)
            .FirstOrDefaultAsync();

        if (examId is null)
        {
            var exam = new Exam
            {
                CertId = certId, Name = $"Drill bank {Guid.NewGuid():N}", Status = "published",
                DurationMinutes = 60, Purpose = purpose,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            examId = exam.ExamId;
        }

        var existing = await db.ExamItems.AsNoTracking()
            .Where(ei => ei.ExamId == examId.Value && ei.SectionId == sectionId)
            .OrderBy(ei => ei.DisplayOrder)
            .Select(ei => new { ei.ExamItemId, ei.ItemId, ei.ItemVersionId })
            .ToListAsync();

        var positions = existing
            .Select(p => (p.ExamItemId, p.ItemId, p.ItemVersionId))
            .ToList();

        for (var i = positions.Count; i < count; i++)
        {
            var position = await PoolFixture.AddMcqAsync(
                db, examId.Value, certId, sectionId, sectionModality, $"Question {i + 1}",
                displayOrder: i + 1, keyLabel: "A", level: "B1");
            positions.Add((position.ExamItemId, position.ItemId, position.ItemVersionId));
        }

        var taken = positions.Take(count).ToList();
        return (examId.Value, taken.Select(p => p.ItemId).ToArray(),
            taken.Select(p => p.ItemVersionId).ToArray(), taken.Select(p => p.ExamItemId).ToArray());
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
        // Remove the placement: a new learner who has only ever practised. The placement is abandoned
        // first, because a done placement may not lose its measured row (trg_PlacementSkillResults_
        // CompletionIsEarned), and then the rows go before the parent (FK_PSR_PlacementCert).
        await database.SqlAsync($"""
            UPDATE PlacementTests SET Status = 'abandoned' WHERE UserId = {userId};
            DELETE FROM PlacementSkillResults
              WHERE PlacementId IN (SELECT PlacementId FROM PlacementTests WHERE UserId = {userId});
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

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var section = await db.Sections.AsNoTracking()
            .Where(s => s.SectionId == sectionId)
            .Select(s => new { s.CertId, s.Modality })
            .SingleAsync();

        // #269: the drill draws pool stock, so the fixture authors one position the drill can serve and
        // addresses the answer the way the pool does - by the version it pinned.
        var bank = await EnsurePoolPositionsAsync(section.CertId, sectionId, section.Modality, 1);
        var versionId = bank.VersionIds[0];

        var learners = new LearnerService(db);
        var practice = new PracticeService(db, learners);
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));

        var before = DateTime.UtcNow.AddSeconds(-1);
        await practice.SubmitAnswerAsync(userId, session.SessionId, versionId, new SubmitPracticeAnswerInput { SelectedOptionId = null });
        var after = DateTime.UtcNow.AddSeconds(1);

        var answer = await db.PracticeAnswers.AsNoTracking()
            .SingleAsync(a => a.SessionId == session.SessionId && a.ItemVersionId == versionId);
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
