using BusinessObjects;

namespace Services.Tests;

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

    [Fact]
    public async Task Readiness_for_a_cold_start_learner_has_null_standing_and_zero_path_steps()
    {
        var code = Unique("RC");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.NotNull(dashboard.TargetReadiness);
        Assert.Null(dashboard.TargetReadiness.CurrentBandCode);
        Assert.Null(dashboard.TargetReadiness.CurrentBandName);
        Assert.Null(dashboard.TargetReadiness.CurrentTotalScore);
        Assert.Null(dashboard.TargetReadiness.CurrentSource);
        Assert.Null(dashboard.TargetReadiness.TargetBandCode);
        Assert.Null(dashboard.TargetReadiness.TargetBandName);
        Assert.Null(dashboard.TargetReadiness.TargetMinScore);
        Assert.Equal(0, dashboard.TargetReadiness.CompletedPathSteps);
        Assert.Equal(0, dashboard.TargetReadiness.TotalPathSteps);
        Assert.Null(dashboard.OverallAccuracyPercent);
    }

    [Fact]
    public async Task Readiness_surfaces_latest_completed_placement_or_graded_mock_standing()
    {
        var code = Unique("RP");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        await SeedCompletedPlacementAsync(userId, code, 385, "LOW", "2026-09-01T10:00:00+07:00");

        var afterPlacement = await CreateService().GetDashboardAsync(userId);
        Assert.NotNull(afterPlacement.TargetReadiness);
        Assert.Equal("placement", afterPlacement.TargetReadiness.CurrentSource);
        Assert.Equal(385, afterPlacement.TargetReadiness.CurrentTotalScore);
        Assert.Equal("LOW", afterPlacement.TargetReadiness.CurrentBandCode);
        Assert.Equal("Low band", afterPlacement.TargetReadiness.CurrentBandName);

        await SeedGradedAttemptAsync(userId, code, 640, "HIGH", "2026-09-10T10:00:00+07:00");

        var afterMock = await CreateService().GetDashboardAsync(userId);
        Assert.NotNull(afterMock.TargetReadiness);
        Assert.Equal("mock", afterMock.TargetReadiness.CurrentSource);
        Assert.Equal(640, afterMock.TargetReadiness.CurrentTotalScore);
        Assert.Equal("HIGH", afterMock.TargetReadiness.CurrentBandCode);
        Assert.Equal("High band", afterMock.TargetReadiness.CurrentBandName);
    }

    private Task SeedTargetBandAndPathAsync(int userId, string certCode, string targetBandCode, int doneSteps, int totalSteps) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            DECLARE @sec INT = (SELECT TOP 1 SectionId FROM Sections WHERE CertId = @cert);

            MERGE TargetBands AS tgt
            USING (SELECT {userId} AS UserId, @cert AS CertId) AS src
            ON tgt.UserId = src.UserId AND tgt.CertId = src.CertId
            WHEN MATCHED THEN UPDATE SET TargetBandCode = '{targetBandCode}'
            WHEN NOT MATCHED THEN INSERT (UserId, CertId, TargetBandCode) VALUES ({userId}, @cert, '{targetBandCode}');

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'LOW', '{targetBandCode}', N'Path {certCode}');
            DECLARE @tpl INT = SCOPE_IDENTITY();

            INSERT INTO PathModules (TemplateId, SkillId, CertId, DisplayOrder, Title)
            VALUES (@tpl, @skill, @cert, 1, N'Module 1');
            DECLARE @mod INT = SCOPE_IDENTITY();

            INSERT INTO LearnerPaths (UserId, CertId, TemplateId, CurrentBandCode, Status)
            VALUES ({userId}, @cert, @tpl, 'LOW', 'active');
            DECLARE @path INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalSteps}
            BEGIN
              INSERT INTO PathModuleItems (ModuleId, CertId, TemplateId, ItemType, RefSectionId, QuestionCount, DisplayOrder)
              VALUES (@mod, @cert, @tpl, 'practice_section', @sec, 5, @i);
              DECLARE @item INT = SCOPE_IDENTITY();

              INSERT INTO LearnerPathSteps (LearnerPathId, PathModuleItemId, TemplateId, Status)
              VALUES (@path, @item, @tpl, CASE WHEN @i <= {doneSteps} THEN 'done' ELSE 'in_progress' END);
              SET @i = @i + 1;
            END
            """);

    [Fact]
    public async Task Readiness_populates_target_band_and_active_learning_path_step_counts()
    {
        var code = Unique("RT");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        await SeedTargetBandAndPathAsync(userId, code, "HIGH", doneSteps: 2, totalSteps: 3);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.NotNull(dashboard.TargetReadiness);
        Assert.Equal("HIGH", dashboard.TargetReadiness.TargetBandCode);
        Assert.Equal("High band", dashboard.TargetReadiness.TargetBandName);
        Assert.Equal(500, dashboard.TargetReadiness.TargetMinScore);
        Assert.Equal(2, dashboard.TargetReadiness.CompletedPathSteps);
        Assert.Equal(3, dashboard.TargetReadiness.TotalPathSteps);
    }

    private Task SeedPracticeAnswersAsync(int userId, string certCode, int correctCount, int totalCount) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            DECLARE @sec INT = (SELECT TOP 1 SectionId FROM Sections WHERE CertId = @cert);
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, FinishedAt)
            VALUES ({userId}, @sec, 'listening', {totalCount}, 'finished', {correctCount}, SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalCount}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, @sec, @skill, CONCAT(N'Q', @i), 2, 'mcq', @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Correct', 1),
                (@q, 'mcq', 'B', N'Wrong', 0);

              DECLARE @isCorr BIT = CASE WHEN @i <= {correctCount} THEN 1 ELSE 0 END;
              DECLARE @opt INT = (SELECT OptionId FROM QuestionOptions WHERE QuestionId = @q AND IsCorrect = @isCorr);

              INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, DisplayOrder)
              VALUES (@sess, @q, @sec, @opt, @isCorr, @i);

              SET @i = @i + 1;
            END
            """);

    [Fact]
    public async Task Readiness_computes_overall_accuracy_percent_from_section_accuracy_view()
    {
        var code = Unique("RA");
        await SeedCertificateAsync(code);
        var userId = await SeedLearnerAsync(code);

        await SeedPracticeAnswersAsync(userId, code, correctCount: 3, totalCount: 4);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(75, dashboard.OverallAccuracyPercent);
    }

    [Fact]
    public async Task Readiness_and_accuracy_isolate_active_certificate_and_current_learner()
    {
        var toeic = Unique("RT");
        var ielts = Unique("RI");
        await SeedCertificateAsync(toeic);
        await SeedCertificateAsync(ielts);

        var mine = await SeedLearnerAsync(toeic);
        var theirs = await SeedLearnerAsync(toeic);

        // Another learner has data on TOEIC
        await SeedCompletedPlacementAsync(theirs, toeic, 700, "HIGH", "2026-09-01T10:00:00+07:00");
        await SeedTargetBandAndPathAsync(theirs, toeic, "HIGH", doneSteps: 4, totalSteps: 5);
        await SeedPracticeAnswersAsync(theirs, toeic, correctCount: 4, totalCount: 4);

        // Current learner has data only on IELTS
        await SeedCompletedPlacementAsync(mine, ielts, 420, "LOW", "2026-09-02T10:00:00+07:00");
        await SeedTargetBandAndPathAsync(mine, ielts, "HIGH", doneSteps: 1, totalSteps: 2);
        await SeedPracticeAnswersAsync(mine, ielts, correctCount: 1, totalCount: 2);

        // On TOEIC, current learner sees empty readiness and null accuracy
        var toeicDash = await CreateService().GetDashboardAsync(mine);
        Assert.NotNull(toeicDash.TargetReadiness);
        Assert.Null(toeicDash.TargetReadiness.CurrentSource);
        Assert.Null(toeicDash.TargetReadiness.CurrentTotalScore);
        Assert.Null(toeicDash.TargetReadiness.TargetBandCode);
        Assert.Equal(0, toeicDash.TargetReadiness.CompletedPathSteps);
        Assert.Equal(0, toeicDash.TargetReadiness.TotalPathSteps);
        Assert.Null(toeicDash.OverallAccuracyPercent);

        // Switching to IELTS shows current learner's IELTS readiness and accuracy
        await CreateService().SwitchCertificateAsync(mine, ielts);
        var ieltsDash = await CreateService().GetDashboardAsync(mine);
        Assert.NotNull(ieltsDash.TargetReadiness);
        Assert.Equal("placement", ieltsDash.TargetReadiness.CurrentSource);
        Assert.Equal(420, ieltsDash.TargetReadiness.CurrentTotalScore);
        Assert.Equal("LOW", ieltsDash.TargetReadiness.CurrentBandCode);
        Assert.Equal("HIGH", ieltsDash.TargetReadiness.TargetBandCode);
        Assert.Equal(1, ieltsDash.TargetReadiness.CompletedPathSteps);
        Assert.Equal(2, ieltsDash.TargetReadiness.TotalPathSteps);
        Assert.Equal(50, ieltsDash.OverallAccuracyPercent);
    }
}
