using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SittingStateMachineTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AttemptEngine CreateEngine()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AttemptEngine(db);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<(int certId, int examId, int skill1Id, int skill2Id, int q1Id, int q2Id)> SeedTwoSkillExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 30, 0, 100, 5),
              (@cert, 'READ', N'Reading', 'reading', 2, 45, 0, 100, 5);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, NavigationMode) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 1, 'FULL_SANDBOX'),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2, 1, 'FULL_SANDBOX');

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 75, 'mock');

            -- #269: the pool's topic vocabulary is a closed set of twenty that no certificate owns, so a
            -- fixture needs one of each modality it authors.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'listening' AND Code = 'L-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-DET', N'Listening for detail');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var sections = await db.Sections.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();

        // #269: the paper is the roster now, so each position is an item and a version bound to it. The id
        // a test addresses is the POSITION's, which is what the engine reads and what an answer names.
        var first = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, sections[0].SectionId, "listening", "Listening Q1", displayOrder: 1);
        var second = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, sections[1].SectionId, "reading", "Reading Q2", displayOrder: 2);

        return (cert.CertId, exam.ExamId, skills[0].SkillId, skills[1].SkillId, first.ExamItemId, second.ExamItemId);
    }

    [Fact]
    public async Task InactiveSkill_Rejects_SaveAnswer_And_SaveDraft()
    {
        var certCode = Unique("SSM1");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        await using var optDb = DbContextFactory.Create(database.ConnectionString);
        var opt1Id = await PoolFixture.OptionIdAsync(optDb, examId, q1Id, "A");
        var opt2Id = await PoolFixture.OptionIdAsync(optDb, examId, q2Id, "A");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);
        Assert.Equal(skill1Id, attempt.ActiveSkillId);
        Assert.Equal(1, attempt.ActiveUnitNumber);
        Assert.NotNull(attempt.PhaseDeadlineAt);

        // Answering Question 1 in active Skill 1 succeeds
        var saveResult = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1Id, false);
        Assert.Equal(q1Id, saveResult.QuestionId);

        // Answering Question 2 in inactive Skill 2 throws WrongPhase
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2Id, false));
        Assert.Equal(AttemptEngineException.WrongPhase, ex.Code);

        // Saving draft to Question 2 in inactive Skill 2 also throws WrongPhase
        var draftEx = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q2Id, "draft"));
        Assert.Equal(AttemptEngineException.WrongPhase, draftEx.Code);
    }

    [Fact]
    public async Task UnitLockedSandbox_Transition_Locks_Preceding_Unit()
    {
        var certCode = Unique("SSM2");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1, 60, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode, ExpectedQuestionCount, UnitCount) VALUES
              (@skill, @cert, 'reading', 'ADAPT_R', N'Adaptive Reading', 1, 'UNIT_LOCKED_SANDBOX', 1, 2);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 60, 'mock');

            INSERT INTO Topics (Modality, Code, Name)
            SELECT 'reading', 'R-DET', N'Local factual retrieval'
            WHERE NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        // #269: a unit is a position's UnitNumber on the roster, not a group row.
        var q1 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Unit 1 Question",
            displayOrder: 1, unitNumber: 1);
        var q2 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Unit 2 Question",
            displayOrder: 2, unitNumber: 2);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.Equal(1, attempt.ActiveUnitNumber);

        var opt1 = await PoolFixture.OptionIdAsync(db, exam.ExamId, q1.ExamItemId, "A");
        var opt2 = await PoolFixture.OptionIdAsync(db, exam.ExamId, q2.ExamItemId, "A");

        // Can answer Unit 1 question
        var res1 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1.ExamItemId, opt1, false);
        Assert.Equal(q1.ExamItemId, res1.QuestionId);

        // Cannot answer Unit 2 question while in Unit 1
        var ex1 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q2.ExamItemId, opt2, false));
        Assert.Equal(AttemptEngineException.WrongPhase, ex1.Code);

        // Advance to Unit 2
        var transition = await engine.TransitionUnitAsync(attempt.AttemptId, 2);
        Assert.Equal(2, transition.ActiveUnitNumber);

        // Now can answer Unit 2 question
        var res2 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q2.ExamItemId, opt2, false);
        Assert.Equal(q2.ExamItemId, res2.QuestionId);

        // Unit 1 is permanently locked: writing to Unit 1 question is now rejected
        var ex2 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1.ExamItemId, opt1, false));
        Assert.Equal(AttemptEngineException.WrongPhase, ex2.Code);

        // Cannot transition backward to Unit 1
        var backEx = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.TransitionUnitAsync(attempt.AttemptId, 1));
        Assert.Equal(AttemptEngineException.WrongPhase, backEx.Code);
    }

    [Fact]
    public async Task HybridGroupLocked_Transition_Locks_Preceding_Group_And_Sets_SubTimer()
    {
        var certCode = Unique("SSM3");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'WRIT', N'Writing', 'writing', 1, 60, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, NavigationMode) VALUES
              (@skill, @cert, 'writing', 'W_HYB', N'Hybrid Writing', 1, 1, 'HYBRID_GROUP_LOCKED');
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 60, 'mock');

            -- #269: a HYBRID_GROUP_LOCKED paper's per-unit clock is the STIMULUS's own window, because a
            -- unit is the block its items share.
            INSERT INTO Topics (Modality, Code, Name)
            SELECT 'writing', 'W-ARG', N'Persuasive argumentation'
            WHERE NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'writing' AND Code = 'W-ARG');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        var picture = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "writing",
            "Picture 1 Question", displayOrder: 1, stimulusResponseSeconds: 480, unitNumber: 1);
        var essay = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "writing",
            "Essay 1 Question", displayOrder: 2, stimulusResponseSeconds: 600, unitNumber: 2);

        var groups = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync();
        var q1 = picture;
        var q2 = essay;
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.NotNull(attempt.PhaseDeadlineAt);

        // 480 seconds deadline
        var initialRemaining = (attempt.PhaseDeadlineAt!.Value - DateTime.UtcNow).TotalSeconds;
        Assert.InRange(initialRemaining, 460, 485);

        // Can save draft to Group 1 question
        var d1 = await engine.SaveDraftAsync(attempt.AttemptId, userId, q1.ExamItemId, "Sentence based on picture");
        Assert.Equal(q1.ExamItemId, d1.QuestionId);

        // Cannot save draft to Group 2 question while Group 1 is active
        var ex1 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q2.ExamItemId, "Essay draft"));
        Assert.Equal(AttemptEngineException.WrongPhase, ex1.Code);

        // Advance to the second unit. On the pool a "group" is the block a position sits in: the STIMULUS
        // when the unit has one (this one carries its own 600-second window), else the negated position id
        // for a solo block. The projection's GroupId is what the client echoes, so the test reads it from
        // there rather than guessing the sign.
        var sitting = await new AttemptEngine(db).GetExamPreviewTakeAsync(exam.ExamId, null);
        var groupKey = sitting.Questions!.Single(q => q.QuestionId == q2.ExamItemId).GroupId!.Value;

        var transition = await engine.TransitionGroupAsync(attempt.AttemptId, groupKey);
        Assert.Equal(groupKey, transition.ActiveGroupId);

        // Sub-timer updated to 600 seconds
        Assert.NotNull(transition.PhaseDeadlineAt);
        var secondRemaining = (transition.PhaseDeadlineAt!.Value - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(secondRemaining, 580, 605);

        // Can save draft to Group 2 question
        var d2 = await engine.SaveDraftAsync(attempt.AttemptId, userId, q2.ExamItemId, "Essay text");
        Assert.Equal(q2.ExamItemId, d2.QuestionId);

        // Preceding Group 1 is permanently locked
        var ex2 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q1.ExamItemId, "Late edit to picture"));
        Assert.Equal(AttemptEngineException.WrongPhase, ex2.Code);
    }

    [Fact]
    public async Task ZeroTimeRollover_Discards_Unused_Time_On_Skill_Transition()
    {
        var certCode = Unique("SSM4");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        await using var optDb = DbContextFactory.Create(database.ConnectionString);
        var opt1Id = await PoolFixture.OptionIdAsync(optDb, examId, q1Id, "A");
        var opt2Id = await PoolFixture.OptionIdAsync(optDb, examId, q2Id, "A");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);
        Assert.Equal(skill1Id, attempt.ActiveSkillId);

        // Initial deadline is ~30 minutes (Skill 1 duration)
        var s1Remaining = (attempt.PhaseDeadlineAt!.Value - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(s1Remaining, 28, 31);

        // Advance early to Skill 2 (Reading, 45 minutes)
        var transition = await engine.TransitionToNextSkillAsync(attempt.AttemptId);
        Assert.Equal(skill2Id, transition.ActiveSkillId);
        Assert.Equal(1, transition.ActiveUnitNumber);

        // Zero rollover: Phase deadline is strictly set to UtcNow + 45 minutes
        var s2Remaining = (transition.PhaseDeadlineAt!.Value - DateTimeOffset.UtcNow).TotalMinutes;
        Assert.InRange(s2Remaining, 43, 46);

        // Question 1 (Skill 1) is now permanently locked
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1Id, false));
        Assert.Equal(AttemptEngineException.WrongPhase, ex.Code);

        // Question 2 (Skill 2) is accepted
        var res = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2Id, false);
        Assert.Equal(q2Id, res.QuestionId);

        // Transitioning beyond the final skill submits the exam
        var finalTransition = await engine.TransitionToNextSkillAsync(attempt.AttemptId);
        Assert.True(finalTransition.Status == AttemptStatus.Graded || finalTransition.Status == AttemptStatus.Submitted);
    }

    [Fact]
    public async Task AudioPlusCheck_ReviewWindow_Initiates_And_AutoLocks_On_Expiration()
    {
        var certCode = Unique("SSM5");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 30, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, NavigationMode) VALUES
              (@skill, @cert, 'listening', 'L_REV', N'IELTS Listening', 1, 1, 'AUDIO_DRIVEN');
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 30, 'mock');

            INSERT INTO Topics (Modality, Code, Name)
            SELECT 'listening', 'L-DET', N'Listening for detail'
            WHERE NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'listening' AND Code = 'L-DET');
            """);

        await using (var dbInit = DbContextFactory.Create(database.ConnectionString))
        {
            var sec = await dbInit.Sections.SingleAsync(s => s.Code == "L_REV");
            sec.TimerPolicyJson = "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}";
            await dbInit.SaveChangesAsync();
        }

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);
        var position = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "listening", "Listening Question", displayOrder: 1);
        var q = position;
        var opt = await PoolFixture.OptionIdAsync(db, exam.ExamId, position.ExamItemId, "A");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 30);

        // Candidate finishes listening playback, starts the review window
        var reviewWindow = await engine.CompleteAudioAndStartReviewAsync(attempt.AttemptId);
        Assert.NotNull(reviewWindow.PhaseDeadlineAt);
        var remainingSec = (reviewWindow.PhaseDeadlineAt!.Value - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(remainingSec, 110, 125);

        // Answering during review window succeeds
        var res = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q.ExamItemId, opt, false);
        Assert.Equal(q.ExamItemId, res.QuestionId);

        // Now simulate expiration of the review window
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET PhaseDeadlineAt = DATEADD(second, -10, GETUTCDATE())
            WHERE AttemptId = {attempt.AttemptId};
            """);

        // Further writes throw AttemptExpired (auto-locked)
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            CreateEngine().SaveAnswerAsync(attempt.AttemptId, userId, q.ExamItemId, opt, false));
        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);
    }

    [Fact]
    public async Task SaveBatchAnswers_Enforces_Sitting_State_Machine()
    {
        var certCode = Unique("SSM6");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        await using var optDb = DbContextFactory.Create(database.ConnectionString);
        var opt1Id = await PoolFixture.OptionIdAsync(optDb, examId, q1Id, "A");
        var opt2Id = await PoolFixture.OptionIdAsync(optDb, examId, q2Id, "A");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // Batch containing answer to inactive skill throws WrongPhase
        var invalidBatch = new List<BatchAnswerItem>
        {
            new(q1Id, opt1Id, false),
            new(q2Id, opt2Id, false)
        };

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveBatchAnswersAsync(attempt.AttemptId, userId, invalidBatch));
        Assert.Equal(AttemptEngineException.WrongPhase, ex.Code);

        // Batch containing only active skill questions succeeds
        var validBatch = new List<BatchAnswerItem>
        {
            new(q1Id, opt1Id, false)
        };

        var batchRes = await engine.SaveBatchAnswersAsync(attempt.AttemptId, userId, validBatch);
        Assert.Single(batchRes);
        Assert.Equal(q1Id, batchRes[0].QuestionId);
    }

    [Fact]
    public async Task GetAttemptTake_Synchronizes_Sitting_State_And_Navigator_Lock_Flags()
    {
        var certCode = Unique("SSM7");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        await using var optDb = DbContextFactory.Create(database.ConnectionString);
        var opt1Id = await PoolFixture.OptionIdAsync(optDb, examId, q1Id, "A");
        var opt2Id = await PoolFixture.OptionIdAsync(optDb, examId, q2Id, "A");
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);
        Assert.Equal(skill1Id, take.ActiveSkillId);
        Assert.Equal(1, take.ActiveUnitNumber);
        Assert.False(take.IsSkillLocked);

        // Question 1 (Skill 1) is unlocked, Question 2 (Skill 2) is locked
        Assert.False(take.Navigator[0].IsLocked);
        Assert.True(take.Navigator[1].IsLocked);

        // Advance to Skill 2
        await engine.TransitionToNextSkillAsync(attempt.AttemptId);

        var take2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 2);
        Assert.Equal(skill2Id, take2.ActiveSkillId);

        // Question 1 is now locked, Question 2 is unlocked
        Assert.True(take2.Navigator[0].IsLocked);
        Assert.False(take2.Navigator[1].IsLocked);
    }

    [Fact]
    public async Task GetAttemptTake_Blocks_Url_Tampering_For_Locked_Phase_And_OneWay_AudioDriven_Questions()
    {
        var certCode = Unique("SSM8");
        var (certId, examId, _, _, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // #269: the second listening question is another pool position in the same section, and the
        // section becomes AUDIO_DRIVEN. A listening item's block is its stimulus, so both share one.
        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            var listening = await seedDb.ExamItems.AsNoTracking()
                .Where(i => i.ExamItemId == q1Id)
                .Select(i => i.SectionId)
                .SingleAsync();

            await seedDb.Database.ExecuteSqlAsync(
                $"UPDATE Sections SET NavigationMode = 'AUDIO_DRIVEN' WHERE SectionId = {listening}");

            // Keep the reading position last so ?q=3 still names it - that is what the phase-tamper check
            // addresses.
            var reading = await seedDb.ExamItems.AsNoTracking()
                .Where(i => i.ExamItemId == q2Id)
                .SingleAsync();
            reading.DisplayOrder = 3;
            await seedDb.SaveChangesAsync();

            // The new listening position goes at 2, which is the one the AUDIO_DRIVEN lock is asserted on.
            await PoolFixture.AddMcqAsync(
                seedDb, examId, certId, listening, "listening", "Listening Q2", displayOrder: 2);
        }

        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // 1. Tampering ?q=3 (Reading skill while Listening is active) must stay on Question 1
        var blockedPhaseJump = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 3);
        Assert.Equal(1, blockedPhaseJump.CurrentQuestion.DisplayOrder);

        // 2. #269: a pool paper resumes at its first unlocked position, so advancing is an explicit act -
        // not a display-order hint. The first write records the position, which is what a real client does.
        var q1Option = await PoolFixture.OptionIdAsync(db(), examId, q1Id, "A");
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, q1Option, false);

        var advancedToQ2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 2);
        Assert.Equal(2, advancedToQ2.CurrentQuestion.DisplayOrder);
        Assert.True(advancedToQ2.Navigator[0].IsLocked);
        Assert.False(advancedToQ2.Navigator[1].IsLocked);

        var blockedRewindToQ1 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
        Assert.Equal(2, blockedRewindToQ1.CurrentQuestion.DisplayOrder);
    }

    [Fact]
    public async Task GetAttemptTake_Allows_StepByStep_Forward_Advance_In_SystemPacedAuto_While_Blocking_Skips_And_Rewinds()
    {
        var certCode = Unique("SSM9");
        var (certId, examId, _, _, q1Id, q2Id) = await SeedTwoSkillExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        await using (var seedDb = DbContextFactory.Create(database.ConnectionString))
        {
            var sectionId = await seedDb.ExamItems.AsNoTracking()
                .Where(i => i.ExamItemId == q1Id)
                .Select(i => i.SectionId)
                .SingleAsync();

            await seedDb.Database.ExecuteSqlAsync(
                $"UPDATE Sections SET NavigationMode = 'SYSTEM_PACED_AUTO' WHERE SectionId = {sectionId}");

            var secondSkillItem = await seedDb.ExamItems
                .Where(i => i.ExamItemId == q2Id)
                .SingleAsync();
            secondSkillItem.DisplayOrder = 4;
            await seedDb.SaveChangesAsync();

            await PoolFixture.AddMcqAsync(
                seedDb, examId, certId, sectionId, "listening", "System Paced Q2", displayOrder: 2);
            await PoolFixture.AddMcqAsync(
                seedDb, examId, certId, sectionId, "listening", "System Paced Q3", displayOrder: 3);
        }

        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // Jumping ahead past Q2 directly to Q3 is blocked in SYSTEM_PACED_AUTO
        var blockedSkipToQ3 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 3);
        Assert.Equal(1, blockedSkipToQ3.CurrentQuestion.DisplayOrder);

        // Advancing one question forward (Q1 -> Q2) succeeds and persists position
        var advancedToQ2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 2);
        Assert.Equal(2, advancedToQ2.CurrentQuestion.DisplayOrder);

        var reloadedAtQ2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: null);
        Assert.Equal(2, reloadedAtQ2.CurrentQuestion.DisplayOrder);

        // Rewinding from Q2 back to Q1 is blocked
        var blockedRewindToQ1 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
        Assert.Equal(2, blockedRewindToQ1.CurrentQuestion.DisplayOrder);

        // Advancing one question forward (Q2 -> Q3) succeeds
        var advancedToQ3 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 3);
        Assert.Equal(3, advancedToQ3.CurrentQuestion.DisplayOrder);
    }

    private CompanionDbContext db() => DbContextFactory.Create(database.ConnectionString);
}
