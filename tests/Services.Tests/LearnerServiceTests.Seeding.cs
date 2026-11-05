using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>Seeding helpers shared by the three-block tests: a completed placement, a target band, and
/// a path whose steps carry week numbers. They are generated SQL because the schema is the source of
/// truth (ADR-0001) and these rows must satisfy the real checks and triggers.</summary>
public sealed partial class LearnerServiceTests
{
    private Task SeedCompletedPlacementAsync(int userId, string certCode, int total, string band, string takenAt) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @cert AND Purpose = 'placement')
              INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Placement {certCode}', 'published', 30, 'placement');
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'placement');
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({userId}, @exam, @cert, 'graded',
                    DATEADD(MINUTE, -30, CAST('{takenAt}' AS DATETIMEOFFSET)),
                    DATEADD(MINUTE, 30, CAST('{takenAt}' AS DATETIMEOFFSET)),
                    '{takenAt}', {total}, '{band}');
            DECLARE @attempt INT = SCOPE_IDENTITY();
            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, ResultBandCode, AttemptId, TotalScore, TakenAt)
            VALUES ({userId}, @cert, @exam, 'done', '{band}', @attempt, {total}, '{takenAt}');
            """);

    private Task SeedSetTargetBandAsync(int userId, string certCode, string targetBandCode) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            MERGE TargetBands AS tgt
            USING (SELECT {userId} AS UserId, @cert AS CertId) AS src
            ON tgt.UserId = src.UserId AND tgt.CertId = src.CertId
            WHEN MATCHED THEN UPDATE SET TargetBandCode = '{targetBandCode}'
            WHEN NOT MATCHED THEN INSERT (UserId, CertId, TargetBandCode) VALUES ({userId}, @cert, '{targetBandCode}');
            """);

    /// <param name="weekSteps">(week number or null, estimated minutes, done).</param>
    private Task SeedGoalWithPathAsync(
        int userId,
        string certCode,
        string targetBandCode,
        string? targetDate,
        decimal? weeklyHours,
        string? feasibility,
        (int? Week, int Minutes, bool Done)[] weekSteps) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            DECLARE @sec INT = (SELECT TOP 1 SectionId FROM Sections WHERE CertId = @cert);

            MERGE TargetBands AS tgt
            USING (SELECT {userId} AS UserId, @cert AS CertId) AS src
            ON tgt.UserId = src.UserId AND tgt.CertId = src.CertId
            WHEN MATCHED THEN UPDATE SET TargetBandCode = '{targetBandCode}',
                TargetDate = {(targetDate is null ? "NULL" : $"CAST('{targetDate}' AS DATE)")},
                WeeklyStudyHours = {(weeklyHours is null ? "NULL" : weeklyHours.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))},
                FeasibilityStatus = {(feasibility is null ? "NULL" : $"'{feasibility}'")}
            WHEN NOT MATCHED THEN INSERT (UserId, CertId, TargetBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
                VALUES ({userId}, @cert, '{targetBandCode}',
                        {(targetDate is null ? "NULL" : $"CAST('{targetDate}' AS DATE)")},
                        {(weeklyHours is null ? "NULL" : weeklyHours.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))},
                        {(feasibility is null ? "NULL" : $"'{feasibility}'")});

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'LOW', '{targetBandCode}', N'Path {certCode}');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title)
            VALUES (@tpl, @skill, @cert, 1, N'Module 1');
            DECLARE @mod INT = SCOPE_IDENTITY();

            INSERT INTO LearnerPaths (UserId, CertId, TemplateId, CurrentBandCode, Status)
            VALUES ({userId}, @cert, @tpl, 'LOW', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();

            """ + string.Concat(weekSteps.Select((s, i) => $"""
            INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
            VALUES (@mod, @cert, @tpl, 'practice_section', @sec, 5, {i + 1});
            DECLARE @item{i} INT = SCOPE_IDENTITY();
            INSERT INTO LearnerPathSteps (LearnerPathId, PathModuleItemId, TemplateId, Status, WeekNumber, EstimatedMinutes)
            VALUES (@path, @item{i}, @tpl, '{(s.Done ? "done" : "available")}',
                    {(s.Week is null ? "NULL" : s.Week.Value.ToString())}, {s.Minutes});
            """)));

    /// <summary>A practice session whose answers carry a category, so the category accuracy view has
    /// a row and the skill card has a weak topic to name.</summary>
    private Task SeedSectionPracticeHistoryAsync(
        int userId,
        string certCode,
        int sectionId,
        int correctCount,
        int totalCount,
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

            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
            VALUES ({userId}, @cert, {sectionId}, @mod, {totalCount}, 'finished', {correctCount},
                    DATEADD(MINUTE, -15, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());
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
}
