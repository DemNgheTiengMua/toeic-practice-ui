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

    private async Task<(int certId, int examId, int skill1Id, int skill2Id, int q1Id, int q2Id, int opt1Id, int opt2Id)> SeedTwoSkillExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 30, 0, 100, 5),
              (@cert, 'READ', N'Reading', 'reading', 2, 45, 0, 100, 5);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 'FULL_SANDBOX'),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2, 'FULL_SANDBOX');
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 75, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber) VALUES
              (@exam, @cert, @secL, 1, 1),
              (@exam, @cert, @secR, 2, 1);
            DECLARE @gL INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @secL);
            DECLARE @gR INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @secR);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@gL, @exam, @secL, @sL, N'Listening Q1', 1, 'mcq', 1),
              (@gR, @exam, @secR, @sR, N'Reading Q2', 1, 'mcq', 2);
            DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @secL);
            DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @secR);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0),
              (@q2, 'mcq', 'A', N'Opt A', 1),
              (@q2, 'mcq', 'B', N'Opt B', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var opt1 = await db.QuestionOptions.Where(o => o.QuestionId == questions[0].QuestionId && o.Label == "A").SingleAsync();
        var opt2 = await db.QuestionOptions.Where(o => o.QuestionId == questions[1].QuestionId && o.Label == "A").SingleAsync();

        return (cert.CertId, exam.ExamId, skills[0].SkillId, skills[1].SkillId, questions[0].QuestionId, questions[1].QuestionId, opt1.OptionId, opt2.OptionId);
    }

    [Fact]
    public async Task InactiveSkill_Rejects_SaveAnswer_And_SaveDraft()
    {
        var certCode = Unique("SSM1");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id, opt1Id, opt2Id) = await SeedTwoSkillExamAsync(certCode);
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

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode, UnitCount) VALUES
              (@skill, @cert, 'reading', 'ADAPT_R', N'Adaptive Reading', 1, 'UNIT_LOCKED_SANDBOX', 2);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber) VALUES
              (@exam, @cert, @sec, 1, 1),
              (@exam, @cert, @sec, 2, 2);
            DECLARE @g1 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND UnitNumber = 1);
            DECLARE @g2 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND UnitNumber = 2);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@g1, @exam, @sec, @skill, N'Unit 1 Question', 1, 'mcq', 1),
              (@g2, @exam, @sec, @skill, N'Unit 2 Question', 1, 'mcq', 2);
            DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 1);
            DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND DisplayOrder = 2);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q2, 'mcq', 'A', N'Opt A', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var q1 = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 1);
        var q2 = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 2);
        var opt1 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q1.QuestionId);
        var opt2 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q2.QuestionId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.Equal(1, attempt.ActiveUnitNumber);

        // Can answer Unit 1 question
        var res1 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1.QuestionId, opt1.OptionId, false);
        Assert.Equal(q1.QuestionId, res1.QuestionId);

        // Cannot answer Unit 2 question while in Unit 1
        var ex1 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q2.QuestionId, opt2.OptionId, false));
        Assert.Equal(AttemptEngineException.WrongPhase, ex1.Code);

        // Advance to Unit 2
        var transition = await engine.TransitionUnitAsync(attempt.AttemptId, 2);
        Assert.Equal(2, transition.ActiveUnitNumber);

        // Now can answer Unit 2 question
        var res2 = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q2.QuestionId, opt2.OptionId, false);
        Assert.Equal(q2.QuestionId, res2.QuestionId);

        // Unit 1 is permanently locked: writing to Unit 1 question is now rejected
        var ex2 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1.QuestionId, opt1.OptionId, false));
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

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode) VALUES
              (@skill, @cert, 'writing', 'W_HYB', N'Hybrid Writing', 1, 'HYBRID_GROUP_LOCKED');
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber, ResponseSeconds) VALUES
              (@exam, @cert, @sec, 1, 1, 480),
              (@exam, @cert, @sec, 2, 1, 600);
            DECLARE @g1 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND DisplayOrder = 1);
            DECLARE @g2 INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND DisplayOrder = 2);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@g1, @exam, @sec, @skill, N'Picture 1 Question', 1, 'free_response', 1),
              (@g2, @exam, @sec, @skill, N'Essay 1 Question', 1, 'free_response', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var groups = await db.QuestionGroups.Where(g => g.ExamId == exam.ExamId).OrderBy(g => g.DisplayOrder).ToListAsync();
        var q1 = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 1);
        var q2 = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId && q.DisplayOrder == 2);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 60);
        Assert.Equal(groups[0].GroupId, attempt.ActiveGroupId);
        Assert.NotNull(attempt.PhaseDeadlineAt);

        // 480 seconds deadline
        var initialRemaining = (attempt.PhaseDeadlineAt!.Value - DateTime.UtcNow).TotalSeconds;
        Assert.InRange(initialRemaining, 460, 485);

        // Can save draft to Group 1 question
        var d1 = await engine.SaveDraftAsync(attempt.AttemptId, userId, q1.QuestionId, "Sentence based on picture");
        Assert.Equal(q1.QuestionId, d1.QuestionId);

        // Cannot save draft to Group 2 question while Group 1 is active
        var ex1 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q2.QuestionId, "Essay draft"));
        Assert.Equal(AttemptEngineException.WrongPhase, ex1.Code);

        // Advance to Group 2
        var transition = await engine.TransitionGroupAsync(attempt.AttemptId, groups[1].GroupId);
        Assert.Equal(groups[1].GroupId, transition.ActiveGroupId);

        // Sub-timer updated to 600 seconds
        Assert.NotNull(transition.PhaseDeadlineAt);
        var secondRemaining = (transition.PhaseDeadlineAt!.Value - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(secondRemaining, 580, 605);

        // Can save draft to Group 2 question
        var d2 = await engine.SaveDraftAsync(attempt.AttemptId, userId, q2.QuestionId, "Essay text");
        Assert.Equal(q2.QuestionId, d2.QuestionId);

        // Preceding Group 1 is permanently locked
        var ex2 = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q1.QuestionId, "Late edit to picture"));
        Assert.Equal(AttemptEngineException.WrongPhase, ex2.Code);
    }

    [Fact]
    public async Task ZeroTimeRollover_Discards_Unused_Time_On_Skill_Transition()
    {
        var certCode = Unique("SSM4");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id, opt1Id, opt2Id) = await SeedTwoSkillExamAsync(certCode);
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

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode) VALUES
              (@skill, @cert, 'listening', 'L_REV', N'IELTS Listening', 1, 'AUDIO_DRIVEN');
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 30, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber) VALUES
              (@exam, @cert, @sec, 1, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@grp, @exam, @sec, @skill, N'Listening Question', 1, 'mcq', 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q, 'mcq', 'A', N'Opt A', 1);
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
        var q = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        var opt = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q.QuestionId);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, cert.CertId, exam.ExamId, 30);

        // Candidate finishes listening playback, starts the review window
        var reviewWindow = await engine.CompleteAudioAndStartReviewAsync(attempt.AttemptId);
        Assert.NotNull(reviewWindow.PhaseDeadlineAt);
        var remainingSec = (reviewWindow.PhaseDeadlineAt!.Value - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.InRange(remainingSec, 110, 125);

        // Answering during review window succeeds
        var res = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q.QuestionId, opt.OptionId, false);
        Assert.Equal(q.QuestionId, res.QuestionId);

        // Now simulate expiration of the review window
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET PhaseDeadlineAt = DATEADD(second, -10, GETUTCDATE())
            WHERE AttemptId = {attempt.AttemptId};
            """);

        // Further writes throw AttemptExpired (auto-locked)
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            CreateEngine().SaveAnswerAsync(attempt.AttemptId, userId, q.QuestionId, opt.OptionId, false));
        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);
    }

    [Fact]
    public async Task SaveBatchAnswers_Enforces_Sitting_State_Machine()
    {
        var certCode = Unique("SSM6");
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id, opt1Id, opt2Id) = await SeedTwoSkillExamAsync(certCode);
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
        var (certId, examId, skill1Id, skill2Id, q1Id, q2Id, opt1Id, opt2Id) = await SeedTwoSkillExamAsync(certCode);
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
        var (certId, examId, _, _, q1Id, _, _, _) = await SeedTwoSkillExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Add a second Listening question and mark Listening section as AUDIO_DRIVEN
        await database.SqlAsync($"""
            DECLARE @secL INT = (SELECT SectionId FROM Questions WHERE QuestionId = {q1Id});
            DECLARE @gL INT = (SELECT GroupId FROM Questions WHERE QuestionId = {q1Id});
            DECLARE @sL INT = (SELECT SkillId FROM Questions WHERE QuestionId = {q1Id});
            UPDATE Sections SET NavigationMode = 'AUDIO_DRIVEN' WHERE SectionId = @secL;

            UPDATE Questions SET DisplayOrder = 3 WHERE ExamId = {examId} AND QuestionId <> {q1Id};
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@gL, {examId}, @secL, @sL, N'Listening Q2', 1, 'mcq', 2);
            DECLARE @qL2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@qL2, 'mcq', 'A', N'Opt A', 1),
              (@qL2, 'mcq', 'B', N'Opt B', 0);
            """);

        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // 1. Tampering ?q=3 (Reading skill while Listening is active) must stay on Question 1
        var blockedPhaseJump = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 3);
        Assert.Equal(1, blockedPhaseJump.CurrentQuestion.DisplayOrder);

        // 2. Advancing to Question 2 in AUDIO_DRIVEN locks Question 1; tampering ?q=1 must stay on Question 2
        var advancedToQ2 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 2);
        Assert.Equal(2, advancedToQ2.CurrentQuestion.DisplayOrder);
        Assert.True(advancedToQ2.Navigator[0].IsLocked);
        Assert.False(advancedToQ2.Navigator[1].IsLocked);

        var blockedRewindToQ1 = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, questionDisplayOrder: 1);
        Assert.Equal(2, blockedRewindToQ1.CurrentQuestion.DisplayOrder);
    }
}
