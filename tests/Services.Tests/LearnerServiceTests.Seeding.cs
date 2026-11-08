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

            -- #241 / #211: a completed placement ALSO writes the learner's per-skill state, which is
            -- the record every screen reads a level from (PlacementResultWriter calls
            -- SkillStateWriter.RecordAsync per measured skill). Writing only PlacementSkillResults
            -- left the fixture in a state the real writer never produces, and the cascade then read
            -- it as nothing-measured. One row per examined skill, so a clean sitting measures the
            -- whole vector. ExpiresAt is MeasuredAt + 90 days exactly, per CK_LSS_Expiry.
            INSERT INTO LearnerSkillStates
              (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate,
               Source, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
            SELECT {userId}, @cert, s.SkillId, 'B1', 50, 3.50,
                   'placement', '{takenAt}', DATEADD(DAY, 90, CAST('{takenAt}' AS DATETIMEOFFSET)),
                   'SkillStateWriter', 1
            FROM Skills s
            WHERE s.CertId = @cert
              AND NOT EXISTS (SELECT 1 FROM LearnerSkillStates l
                              WHERE l.UserId = {userId} AND l.CertId = @cert AND l.SkillId = s.SkillId);
            """);

    /// <summary>A published placement paper, so "how long it takes" has a stored duration to state
    /// rather than an invented one (LRN-13).</summary>
    private Task SeedPlacementExamAsync(string certCode, int minutes) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            IF NOT EXISTS (SELECT 1 FROM Exams WHERE CertId = @cert AND Purpose = 'placement')
              INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Placement {certCode}', 'published', {minutes}, 'placement');
            """);

    private Task SeedSetTargetBandAsync(int userId, string certCode, string targetBandCode) =>        database.SqlAsync($"""
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
            -- #241: the step's modality comes from the skill it practises, not from a hardcoded
            -- 'reading'. FK_Steps_SkillModality is (SkillId, Modality), so a fixture that assumed the
            -- first skill was reading failed on any certificate whose first skill is listening - a
            -- fixture defect that looked like a service defect.
            DECLARE @mod VARCHAR(10) = (SELECT Modality FROM Skills WHERE SkillId = @skill);
            -- #232 / #183: the canonical twenty are a CLOSED set with a trigger refusing an invented
            -- code, so a fixture may not mint one. This picks the first canonical topic for the skill's
            -- own modality - R-DET for reading, L-DET for listening - and the WHERE NOT EXISTS keeps a
            -- repeat call from growing the set.
            DECLARE @code VARCHAR(10) = CASE @mod
                WHEN 'listening' THEN 'L-DET'
                WHEN 'reading' THEN 'R-DET'
                WHEN 'speaking' THEN 'S-DESC'
                ELSE 'W-CORR' END;
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = @mod AND Code = @code)
              INSERT INTO Topics (Modality, Code, Name)
              VALUES (@mod, @code, N'Fixture topic');

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
            VALUES (@path, @cert, @skill, @mod,
                    (SELECT TOP 1 TopicId FROM Topics WHERE Modality = @mod),
                    'B1', 'focus_practice', 'developing',
                    '{(s.Done ? "done" : "available")}',
                    {(s.Week is null ? "NULL" : s.Week.Value.ToString())}, {i + 1}, {s.Minutes},
                    {(s.Done ? "'PathStepCompletion'" : "NULL")}, {(s.Done ? 1 : 0)});
            """)));

    /// <summary>Records one skill's measured state, as the placement writer does. Used by tests that
    /// need a PARTIAL vector: #241's cascade treats an unmeasured skill as a task, so a fixture with
    /// three measured skills and one not is the only way to prove step 2 fires above step 3.</summary>
    private Task SeedSkillStateAsync(
        int userId, string certCode, string skillCode, string level, int position, decimal coordinate,
        string measuredAt) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = '{skillCode}');
            MERGE LearnerSkillStates AS tgt
            USING (SELECT {userId} AS UserId, @cert AS CertId, @skill AS SkillId) AS src
            ON tgt.UserId = src.UserId AND tgt.CertId = src.CertId AND tgt.SkillId = src.SkillId
            WHEN MATCHED THEN UPDATE SET
                CefrLevel = '{level}', PositionInBand = {position},
                ContinuousCoordinate = {coordinate.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                Source = 'placement', MeasuredAt = '{measuredAt}',
                ExpiresAt = DATEADD(DAY, 90, CAST('{measuredAt}' AS DATETIMEOFFSET)),
                WrittenByService = 'SkillStateWriter', ProjectionVersion = tgt.ProjectionVersion + 1
            WHEN NOT MATCHED THEN INSERT
                (UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate,
                 Source, MeasuredAt, ExpiresAt, WrittenByService, ProjectionVersion)
                VALUES ({userId}, @cert, @skill, '{level}', {position},
                        {coordinate.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                        'placement', '{measuredAt}',
                        DATEADD(DAY, 90, CAST('{measuredAt}' AS DATETIMEOFFSET)),
                        'SkillStateWriter', 1);
            """);

    /// <summary>A finished practice session with answers, which is what the daily-ceiling view counts.
    /// The cascade reads it, so a test that needs "the day is full" seeds it rather than a stored
    /// counter - there is none (D12).</summary>
    private Task SeedAnsweredTodayAsync(int userId, string certCode, int sectionId, int count) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT SkillId FROM Sections WHERE SectionId = {sectionId});
            DECLARE @mod VARCHAR(10) = (SELECT Modality FROM Sections WHERE SectionId = {sectionId});
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, {sectionId}, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
            VALUES ({userId}, @cert, {sectionId}, @mod, {count}, 'finished', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();
            DECLARE @i INT = 1;
            WHILE @i <= {count}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, {sectionId}, @skill, CONCAT(N'Stem ', @i), 'B1', 'mcq', @i);
              DECLARE @q INT = SCOPE_IDENTITY();
              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Right', 1), (@q, 'mcq', 'B', N'Wrong', 0);
              -- #241: UTC, not local. vw_DailyPracticeVolume groups on CAST(AnsweredAt AS DATE) and the
              -- service compares that against the UTC day, so a local-offset timestamp would land on
              -- tomorrow for a learner east of Greenwich and the ceiling would read as not reached.
              INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, DisplayOrder, AnsweredAt)
              VALUES (@sess, @q, {sectionId},
                      (SELECT OptionId FROM QuestionOptions WHERE QuestionId = @q AND IsCorrect = 0),
                      0, @i, SYSUTCDATETIME());
              SET @i = @i + 1;
            END
            """);

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
