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
            INSERT INTO PlacementTests (UserId, CertId, ExamId, Status, AttemptId, TakenAt, ResultBandCode, TotalScore,
                                        ListeningPct, ReadingPct, SpeakingPct, WritingPct)
            VALUES ({userId}, @cert, @exam, 'done', @attempt, '{takenAt}', '{band}', {total},
                    60, 60, 60, 60);
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
                                          Status, WeekNumber, WeekSequence, EstimatedMinutes)
            VALUES (@path, @cert, @skill, @mod,
                    (SELECT TOP 1 TopicId FROM Topics WHERE Modality = @mod),
                    'B1', 'focus_practice', 'developing',
                    '{(s.Done ? "done" : "available")}',
                    {(s.Week is null ? "NULL" : s.Week.Value.ToString())}, {i + 1}, {s.Minutes});
            """)));

    private Task SeedSkillStateAsync(
        int userId, string certCode, string skillCode, string level, int position, decimal coordinate,
        string measuredAt)
    {
        decimal pct = level switch
        {
            "C2" or "C1" => 90m,
            "B2" => 75m,
            "B1" => 60m,
            "A2" => 45m,
            "A1" => 25m,
            _ => 10m
        };
        return database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @mod NVARCHAR(32) = (SELECT LOWER(Modality) FROM Skills WHERE CertId = @cert AND Code = '{skillCode}');
            DECLARE @plc INT = (SELECT TOP 1 PlacementId FROM PlacementTests WHERE UserId = {userId} AND CertId = @cert AND Status = 'done' ORDER BY PlacementId DESC);
            IF @plc IS NULL
            BEGIN
              INSERT INTO PlacementTests (UserId, CertId, Status, TakenAt)
              VALUES ({userId}, @cert, 'done', '{measuredAt}');
              SET @plc = SCOPE_IDENTITY();
            END
            UPDATE PlacementTests SET
              TakenAt = '{measuredAt}',
              ListeningPct = CASE WHEN @mod = 'listening' THEN {pct.ToString(System.Globalization.CultureInfo.InvariantCulture)} ELSE ListeningPct END,
              ReadingPct = CASE WHEN @mod = 'reading' THEN {pct.ToString(System.Globalization.CultureInfo.InvariantCulture)} ELSE ReadingPct END,
              SpeakingPct = CASE WHEN @mod = 'speaking' THEN {pct.ToString(System.Globalization.CultureInfo.InvariantCulture)} ELSE SpeakingPct END,
              WritingPct = CASE WHEN @mod = 'writing' THEN {pct.ToString(System.Globalization.CultureInfo.InvariantCulture)} ELSE WritingPct END
            WHERE PlacementId = @plc;
            """);
    }

    /// <summary>A finished practice session with answers, which is what the daily-ceiling view counts.
    /// The cascade reads it, so a test that needs "the day is full" seeds it rather than a stored
    /// counter - there is none (D12).
    /// <para>
    /// #272: the answers are pool-shaped. <c>vw_DailyPracticeVolume</c> counts an answer by its own
    /// <c>SelectedLabel</c>/<c>ResponseText</c> and reads the modality through <c>Items</c>, so no
    /// <c>Questions</c> row is needed - which is what lets this fixture stop authoring one.
    /// </para>
    /// </summary>
    private Task SeedAnsweredTodayAsync(int userId, string certCode, int sectionId, int count) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @mod VARCHAR(10) = (SELECT Modality FROM Sections WHERE SectionId = {sectionId});
            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
            VALUES ({userId}, @cert, {sectionId}, @mod, {count}, 'finished', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = @mod ORDER BY TopicId);
            IF @topic IS NULL
            BEGIN
              INSERT INTO Topics (Modality, Code, Name) VALUES (@mod, 'R-DET', N'Local factual retrieval and paraphrase matching');
              SET @topic = SCOPE_IDENTITY();
            END;

            DECLARE @i INT = 1;
            WHILE @i <= {count}
            BEGIN
              INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus)
              VALUES (@mod, @topic, 'B1',
                      CASE WHEN @mod IN ('speaking','writing') THEN 'descriptive_monologue' ELSE 'sentence_cloze' END,
                      CASE WHEN @mod IN ('speaking','writing') THEN 'free_response' ELSE 'mcq' END,
                      'demo', 'active');
              DECLARE @item INT = SCOPE_IDENTITY();
              INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
              VALUES (@item, 1, CONCAT(N'Stem ', @i), N'Right', N'Wrong', N'Wrong', N'Wrong', 'A');
              DECLARE @ver INT = SCOPE_IDENTITY();
              -- #241: UTC, not local. vw_DailyPracticeVolume groups on CAST(AnsweredAt AS DATE) and the
              -- service compares that against the UTC day, so a local-offset timestamp would land on
              -- tomorrow for a learner east of Greenwich and the ceiling would read as not reached.
              INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder, AnsweredAt)
              VALUES (@sess, @item, @ver, 'B', 0, @i, SYSUTCDATETIME());
              SET @i = @i + 1;
            END
            """);

    /// <summary>
    /// A finished practice drill at one pool cell - <paramref name="topicCode"/> at
    /// <paramref name="level"/> - with <paramref name="totalCount"/> graded answers of which
    /// <paramref name="correctCount"/> are right. Returns the topic's stored name, so a test asserts on
    /// the row it seeded rather than on a name another fixture may have chosen for the same code.
    /// <para>
    /// <b>#271: pool-shaped, because the accuracy axis is.</b> It used to author legacy <c>Questions</c>
    /// rows carrying a <c>CategoryId</c>; the dashboard card now reads <c>vw_UserTopicAccuracy</c>,
    /// which reaches a cell only through <c>Items</c>. A legacy answer would therefore render an empty
    /// card - and the test would pass on an assertion about nothing.
    /// </para>
    /// </summary>
    private async Task<string> SeedCellPracticeHistoryAsync(
        int userId, string certCode, int sectionId, int correctCount, int totalCount, string topicCode, string level = "B1")
    {
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @mod VARCHAR(10) = (SELECT Modality FROM Sections WHERE SectionId = {sectionId});

            -- The vocabulary is a closed twenty. A code this database has not seen yet needs its row;
            -- trg_Topics_ClosedVocabulary is what keeps an invented one out.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = @mod AND Code = '{topicCode}')
              INSERT INTO Topics (Modality, Code, Name) VALUES (@mod, '{topicCode}', N'{topicCode}');

            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = @mod AND Code = '{topicCode}');

            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount, Status, CorrectCount, StartedAt, FinishedAt)
            VALUES ({userId}, @cert, {sectionId}, @mod, {totalCount}, 'finished', {correctCount},
                    DATEADD(MINUTE, -15, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalCount}
            BEGIN
              INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, RenderFormat)
              VALUES (@mod, @topic, '{level}',
                      CASE WHEN @mod = 'reading' THEN 'sentence_cloze' ELSE 'short_exchange' END,
                      'demo', 'active', 'mcq');
              DECLARE @item INT = SCOPE_IDENTITY();

              INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, KeyLabel)
              VALUES (@item, 1, CONCAT(N'Stem ', @i), N'Right', N'Wrong', 'A');
              DECLARE @ver INT = SCOPE_IDENTITY();

              DECLARE @isCorr BIT = CASE WHEN @i <= {correctCount} THEN 1 ELSE 0 END;
              INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
              VALUES (@sess, @item, @ver, CASE WHEN @isCorr = 1 THEN 'A' ELSE 'B' END, @isCorr, @i);

              SET @i = @i + 1;
            END
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Topics.Where(t => t.Code == topicCode).Select(t => t.Name).SingleAsync();
    }
}
