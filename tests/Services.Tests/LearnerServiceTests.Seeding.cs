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
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
            VALUES ({userId}, @exam, @cert, 'graded',
                    DATEADD(MINUTE, -30, CAST('{takenAt}' AS DATETIMEOFFSET)),
                    DATEADD(MINUTE, 30, CAST('{takenAt}' AS DATETIMEOFFSET)),
                    '{takenAt}', {total}, '{band}', 'AttemptEngine', 1);
            DECLARE @attempt INT = SCOPE_IDENTITY();
            -- #212: a done placement needs a measured skill or a recorded completion failure, and the
            -- per-skill row is what the result screen reads. Rows first, then the move to done.
            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, AttemptId, TakenAt)
            VALUES ({userId}, @cert, @exam, 'in_progress', @attempt, '{takenAt}');
            DECLARE @plc INT = SCOPE_IDENTITY();
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            INSERT INTO PlacementSkillResults
              (PlacementId, SkillId, CertId, ResultStatus, CefrLevel, PositionInBand, ContinuousCoordinate,
               MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
            VALUES (@plc, @skill, @cert, 'measured', 'B1', 50, 3.50,
                    '{takenAt}', DATEADD(DAY, 90, CAST('{takenAt}' AS DATETIMEOFFSET)), 'AttemptEngine', 1);
            UPDATE PlacementTests
              SET Status = 'done', ResultBandCode = '{band}', TotalScore = {total},
                  WrittenByService = 'AttemptEngine', ProjectionVersion = 1
            WHERE PlacementId = @plc;
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
            -- #232: the step's topic coordinate. A test fixture may create the topic it needs, because
            -- the canonical twenty are a closed set the shared database already holds only in the seed.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Detail');

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

            -- #232: a path is a coordinate set, not a template. No template, module or module item.
            INSERT INTO LearnerPaths (UserId, CertId, CurrentBandCode, Status)
            VALUES ({userId}, @cert, 'LOW', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();

            """ + string.Concat(weekSteps.Select((s, i) => $"""
            INSERT INTO LearnerPathSteps (LearnerPathId, CertId, SkillId, Modality, TopicId, TargetCefr, StepType, SizingTier,
                                          Status, WeekNumber, WeekSequence, EstimatedMinutes,
                                          WrittenByService, ProjectionVersion)
            VALUES (@path, @cert, @skill, 'reading',
                    (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading'),
                    'B1', 'focus_practice', 'developing',
                    '{(s.Done ? "done" : "available")}',
                    {(s.Week is null ? "NULL" : s.Week.Value.ToString())}, {i + 1}, {s.Minutes},
                    {(s.Done ? "'PathStepCompletion'" : "NULL")}, {(s.Done ? 1 : 0)});
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
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, CategoryId, DisplayOrder)
              VALUES (@grp, @exam, {sectionId}, @skill, CONCAT(N'Stem ', @i), 'B1', 'mcq', @cat, @i);
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
