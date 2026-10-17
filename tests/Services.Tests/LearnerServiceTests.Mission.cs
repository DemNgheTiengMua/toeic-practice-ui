using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

public sealed partial class LearnerServiceTests
{
    private async Task<(int Section1Id, int Section2Id)> SeedTwoSectionCertificateAsync(string code)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
            DECLARE @listSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @readSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@listSkill, @cert, 'listening', 'P1', N'Photographs {code}', 1),
              (@readSkill, @cert, 'reading', 'P5', N'Incomplete Sentences {code}', 2);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low band', 0, 499, 1),
              (@cert, 'HIGH', N'High band', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Mock {code}', 'published', 120, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var sections = await db.Sections
            .Where(s => s.Skill.Cert.Code == code)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => s.SectionId)
            .ToListAsync();
        return (sections[0], sections[1]);
    }

    private Task SeedSectionPracticeHistoryAsync(
        int userId,
        string certCode,
        int sectionId,
        int correctCount,
        int totalCount,
        int daysAgo = 0,
        string? categoryCode = null,
        string? categoryName = null) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT SkillId FROM Sections WHERE SectionId = {sectionId});
            DECLARE @mod VARCHAR(10) = (SELECT Modality FROM Sections WHERE SectionId = {sectionId});
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');
            DECLARE @cat INT = NULL;

            IF {(categoryCode is null ? "0 = 1" : "1 = 1")}
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM QuestionCategories WHERE CertId = @cert AND Code = '{categoryCode}')
                INSERT INTO QuestionCategories (CertId, Code, Name) VALUES (@cert, '{categoryCode}', N'{categoryName ?? categoryCode}');
              SELECT @cat = CategoryId FROM QuestionCategories WHERE CertId = @cert AND Code = '{categoryCode}';
            END

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, {sectionId}, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            DECLARE @ts DATETIMEOFFSET = DATEADD(DAY, -{daysAgo}, SYSDATETIMEOFFSET());
            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
            VALUES ({userId}, {sectionId}, @mod, {totalCount}, 'finished', {correctCount}, DATEADD(MINUTE, -15, @ts), @ts);
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalCount}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, CategoryId, DisplayOrder)
              VALUES (@grp, @exam, {sectionId}, @skill, CONCAT(N'Stem ', @i), 2, 'mcq', @cat, @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Right', 1),
                (@q, 'mcq', 'B', N'Wrong', 0);

              DECLARE @isCorr BIT = CASE WHEN @i <= {correctCount} THEN 1 ELSE 0 END;
              DECLARE @opt INT = (SELECT OptionId FROM QuestionOptions WHERE QuestionId = @q AND IsCorrect = @isCorr);

              INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, DisplayOrder)
              VALUES (@sess, @q, {sectionId}, @opt, @isCorr, @i);

              SET @i = @i + 1;
            END
            """);

    private async Task<int> SeedSkillPracticeExamAsync(string certCode, string purpose, string name)
    {
        var modality = purpose == "speaking_practice" ? "speaking" : "writing";
        var skillCode = purpose == "speaking_practice" ? "SPK" : "WRT";
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = @cert AND Code = '{skillCode}')
              INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
              VALUES (@cert, '{skillCode}', N'{skillCode}', '{modality}', 3);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'{name}', 'published', 20, '{purpose}');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Exams
            .Where(e => e.Cert.Code == certCode && e.Name == name)
            .Select(e => e.ExamId)
            .SingleAsync();
    }

    private async Task<int> SeedSkillPracticeAttemptAsync(
        int userId,
        string certCode,
        int examId,
        string status,
        int hoursAgo = 1)
    {
        var submittedSql = status is "submitted" or "graded"
            ? $"DATEADD(HOUR, -{hoursAgo}, SYSDATETIMEOFFSET())"
            : "NULL";

        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @start DATETIMEOFFSET = DATEADD(HOUR, -({hoursAgo} + 1), SYSDATETIMEOFFSET());
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt)
            VALUES ({userId}, {examId}, @cert, '{status}', @start, DATEADD(HOUR, 2, @start), {submittedSql});
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.ExamAttempts
            .Where(a => a.UserId == userId && a.ExamId == examId)
            .OrderByDescending(a => a.AttemptId)
            .Select(a => a.AttemptId)
            .FirstAsync();
    }

    private Task SeedSavedQuestionsAsync(int userId, string certCode, int sectionId, int count) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT SkillId FROM Sections WHERE SectionId = {sectionId});
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, {sectionId}, 99);
            DECLARE @grp INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {count}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, {sectionId}, @skill, CONCAT(N'Saved Q', @i), 2, 'mcq', @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO SavedQuestions (UserId, QuestionId, Source)
              VALUES ({userId}, @q, 'incorrect');

              SET @i = @i + 1;
            END
            """);

    [Fact]
    public async Task Mission_for_cold_start_learner_recommends_placement_secondary_section_and_clean_trap_ledger()
    {
        var code = Unique("MC");
        await SeedTwoSectionCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(3, dashboard.MissionSteps.Count);

        var step1 = dashboard.MissionSteps[0];
        Assert.Equal(1, step1.StepNumber);
        Assert.Equal("/placement", step1.ActionUrl);
        Assert.False(step1.IsCleared);

        var step2 = dashboard.MissionSteps[1];
        Assert.Equal(2, step2.StepNumber);
        Assert.Equal("/practice", step2.ActionUrl);
        Assert.Contains($"Incomplete Sentences {code}", step2.Title);
        Assert.False(step2.IsCleared);

        var step3 = dashboard.MissionSteps[2];
        Assert.Equal(3, step3.StepNumber);
        Assert.Equal("TRAP LEDGER CLEAN", step3.BadgeLabel);
        Assert.Equal("/practice", step3.ActionUrl);
        Assert.True(step3.IsCleared);
        Assert.Contains("practice", step3.Description, StringComparison.OrdinalIgnoreCase);

        // Once placement is completed (still 0 practice/category accuracy rows), Step 1 switches to starter section drill
        await SeedCompletedPlacementAsync(userId, code, 410, "LOW", "2026-09-01T10:00:00+07:00");
        var afterPlacement = await CreateService().GetDashboardAsync(userId);
        var postPlacementStep1 = afterPlacement.MissionSteps[0];
        Assert.Equal(1, postPlacementStep1.StepNumber);
        Assert.Equal("/practice", postPlacementStep1.ActionUrl);
        Assert.Contains($"Photographs {code}", postPlacementStep1.Title);
        Assert.False(postPlacementStep1.IsCleared);
    }

    [Fact]
    public async Task Mission_step1_picks_lowest_accuracy_with_tie_breakers_and_tracks_completed_today()
    {
        var code = Unique("MW");
        var (sec1, sec2) = await SeedTwoSectionCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        // When both sections have equal accuracy (50%) and equal Answered count (4), DisplayOrder tie-breaker picks sec1 (DisplayOrder = 1)
        await SeedSectionPracticeHistoryAsync(userId, code, sec1, correctCount: 2, totalCount: 4, daysAgo: 2);
        await SeedSectionPracticeHistoryAsync(userId, code, sec2, correctCount: 2, totalCount: 4, daysAgo: 2);

        var equalTieDash = await CreateService().GetDashboardAsync(userId);
        Assert.Contains($"Photographs {code}", equalTieDash.MissionSteps[0].Title);

        // Now add 6 more answers (3 correct) to sec2 from 2 days ago so sec2 has 5/10 (50%) vs sec1's 2/4 (50%) -> sec2 wins Answered tie-breaker
        await SeedSectionPracticeHistoryAsync(userId, code, sec2, correctCount: 3, totalCount: 6, daysAgo: 2);

        var dashboard = await CreateService().GetDashboardAsync(userId);
        var step1 = dashboard.MissionSteps[0];

        Assert.Equal(1, step1.StepNumber);
        Assert.Equal("/practice", step1.ActionUrl);
        Assert.Contains($"Incomplete Sentences {code}", step1.Title);
        Assert.Contains("50%", step1.Eyebrow);
        Assert.False(step1.IsCleared);

        // Step 2 falls back to second-weakest section (sec1) since no speaking/writing exam is published
        var step2 = dashboard.MissionSteps[1];
        Assert.Equal(2, step2.StepNumber);
        Assert.Equal("/practice", step2.ActionUrl);
        Assert.Contains($"Photographs {code}", step2.Title);

        // Completing a practice session today in sec2 marks Step 1 IsCleared = true (while keeping sec2 lowest accuracy at 40%)
        await SeedSectionPracticeHistoryAsync(userId, code, sec2, correctCount: 1, totalCount: 6, daysAgo: 0, categoryCode: $"CAT_{code}", categoryName: $"Verb Tense {code}");

        var afterTodayDrill = await CreateService().GetDashboardAsync(userId);
        var clearedStep1 = afterTodayDrill.MissionSteps[0];
        Assert.Contains($"Verb Tense {code}", clearedStep1.Title);
        Assert.Contains($"Incomplete Sentences {code}", clearedStep1.Title);
        Assert.True(clearedStep1.IsCleared);
    }

    [Fact]
    public async Task Mission_step2_reflects_speaking_and_writing_exam_and_attempt_lifecycle()
    {
        var code = Unique("MS");
        await SeedTwoSectionCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var speakingExamId = await SeedSkillPracticeExamAsync(code, "speaking_practice", $"Speaking Drill {code}");

        // 1. Published exam with no recent attempt -> recommends starting exam
        var initial = await CreateService().GetDashboardAsync(userId);
        var step2Start = initial.MissionSteps[1];
        Assert.Equal($"/speaking/instructions/{speakingExamId}", step2Start.ActionUrl);
        Assert.False(step2Start.IsCleared);

        // 2. In-progress attempt -> prompts to resume
        var speakingAttemptId = await SeedSkillPracticeAttemptAsync(userId, code, speakingExamId, "in_progress", hoursAgo: 1);
        var inProgress = await CreateService().GetDashboardAsync(userId);
        var step2Resume = inProgress.MissionSteps[1];
        Assert.Equal($"/speaking/take/{speakingAttemptId}", step2Resume.ActionUrl);
        Assert.False(step2Resume.IsCleared);

        // 3. Submitted attempt (AI grading pending) -> shows STATUS: GRADING QUEUED linking to /writing/pending/{id}
        var writingExamId = await SeedSkillPracticeExamAsync(code, "writing_practice", $"Writing Task {code}");
        var writingSubmittedId = await SeedSkillPracticeAttemptAsync(userId, code, writingExamId, "submitted", hoursAgo: 0);
        var submitted = await CreateService().GetDashboardAsync(userId);
        var step2Pending = submitted.MissionSteps[1];
        Assert.Equal($"/writing/pending/{writingSubmittedId}", step2Pending.ActionUrl);
        Assert.Contains("STATUS: GRADING QUEUED", step2Pending.MetaLabel);
        Assert.False(step2Pending.IsCleared);

        // 4. Graded within last 24h -> IsCleared = true and links to /writing/review/{id}
        await database.SqlAsync($"UPDATE ExamAttempts SET Status = 'graded', SubmittedAt = SYSDATETIMEOFFSET() WHERE AttemptId = {writingSubmittedId};");
        var graded = await CreateService().GetDashboardAsync(userId);
        var step2Graded = graded.MissionSteps[1];
        Assert.Equal($"/writing/review/{writingSubmittedId}", step2Graded.ActionUrl);
        Assert.True(step2Graded.IsCleared);

        // 5. Graded older than 24h -> recommends starting published skill practice exam again
        await database.SqlAsync($"UPDATE ExamAttempts SET Status = 'graded', StartedAt = DATEADD(HOUR, -36, SYSDATETIMEOFFSET()), SubmittedAt = DATEADD(HOUR, -30, SYSDATETIMEOFFSET()) WHERE UserId = {userId};");
        var olderGraded = await CreateService().GetDashboardAsync(userId);
        var step2Restart = olderGraded.MissionSteps[1];
        Assert.Equal($"/speaking/instructions/{speakingExamId}", step2Restart.ActionUrl);
        Assert.False(step2Restart.IsCleared);
    }

    [Fact]
    public async Task Mission_step3_counts_saved_questions_and_isolates_certificate_and_learner()
    {
        var toeic = Unique("MT");
        var ielts = Unique("MI");
        var (toeicSec1, _) = await SeedTwoSectionCertificateAsync(toeic);
        var (ieltsSec1, _) = await SeedTwoSectionCertificateAsync(ielts);

        var mine = await SeedLearnerAsync(toeic);
        var theirs = await SeedLearnerAsync(toeic);

        // Another learner has saved questions, weakness history, and speaking attempt on TOEIC
        var toeicSpkExamId = await SeedSkillPracticeExamAsync(toeic, "speaking_practice", $"TOEIC Spk {toeic}");
        await SeedSectionPracticeHistoryAsync(theirs, toeic, toeicSec1, correctCount: 0, totalCount: 5, daysAgo: 0);
        await SeedSkillPracticeAttemptAsync(theirs, toeic, toeicSpkExamId, "in_progress", hoursAgo: 1);
        await SeedSavedQuestionsAsync(theirs, toeic, toeicSec1, count: 4);

        // Current learner has 3 saved questions on TOEIC and 0 on IELTS
        await SeedSavedQuestionsAsync(mine, toeic, toeicSec1, count: 3);
        await SeedSectionPracticeHistoryAsync(mine, ielts, ieltsSec1, correctCount: 1, totalCount: 5, daysAgo: 1);

        var toeicDash = await CreateService().GetDashboardAsync(mine);
        Assert.Equal(3, toeicDash.MissionSteps.Count);

        // Other learner's TOEIC weakness & speaking attempt did not leak to mine
        Assert.Equal("/placement", toeicDash.MissionSteps[0].ActionUrl);
        Assert.Equal($"/speaking/instructions/{toeicSpkExamId}", toeicDash.MissionSteps[1].ActionUrl);

        // Current learner's 3 saved questions on TOEIC appear on Step 3
        var toeicStep3 = toeicDash.MissionSteps[2];
        Assert.Equal("TRAP LEDGER", toeicStep3.BadgeLabel);
        Assert.Equal("Reconcile 3 Saved Questions", toeicStep3.Title);
        Assert.Equal("/saved-questions", toeicStep3.ActionUrl);
        Assert.False(toeicStep3.IsCleared);

        // Switching to IELTS isolates saved questions (0 on IELTS) and surfaces IELTS weakness
        await CreateService().SwitchCertificateAsync(mine, ielts);
        var ieltsDash = await CreateService().GetDashboardAsync(mine);

        Assert.Equal("/practice", ieltsDash.MissionSteps[0].ActionUrl);
        Assert.Contains($"Photographs {ielts}", ieltsDash.MissionSteps[0].Title);

        var ieltsStep3 = ieltsDash.MissionSteps[2];
        Assert.Equal("TRAP LEDGER CLEAN", ieltsStep3.BadgeLabel);
        Assert.Equal("/practice", ieltsStep3.ActionUrl);
        Assert.True(ieltsStep3.IsCleared);
    }
}

