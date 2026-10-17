namespace Services.Tests;

public sealed partial class LearnerServiceTests
{
    private async Task SeedToeicSimulatorCatalogAsync(string code)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'TOEIC {code}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2),
              (@cert, 'SPEA', N'Speaking', 'speaking', 3),
              (@cert, 'WRIT', N'Writing', 'writing', 4);

            DECLARE @listSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @readSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@listSkill, @cert, 'listening', 'P1', N'Photographs', 1),
              (@readSkill, @cert, 'reading', 'P5', N'Incomplete Sentences', 2);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'below_A1', N'Below A1', 10, 119, 0),
              (@cert, 'A1', N'Beginner', 120, 220, 1),
              (@cert, 'A2', N'Elementary', 225, 545, 2),
              (@cert, 'B1', N'Intermediate', 550, 780, 3),
              (@cert, 'B2', N'Upper intermediate', 785, 940, 4),
              (@cert, 'C1', N'Advanced', 945, 990, 5);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@cert, N'Mock {code}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @listSkill, @exam, 0, 5),
              (@cert, @listSkill, @exam, 1, 250),
              (@cert, @listSkill, @exam, 2, 495),
              (@cert, @readSkill, @exam, 0, 5),
              (@cert, @readSkill, @exam, 1, 250),
              (@cert, @readSkill, @exam, 2, 495);
            """);
    }

    private async Task SeedFourSkillNoScaleCatalogAsync(string code)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'VSTEP {code}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2),
              (@cert, 'WRIT', N'Writing', 'writing', 3),
              (@cert, 'SPEA', N'Speaking', 'speaking', 4);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'A2 (4.0)', 40, 45, 1),
              (@cert, 'B1', N'B1 (5.5-6.0)', 50, 60, 2),
              (@cert, 'B2', N'B2 (6.5-7.5)', 65, 75, 3),
              (@cert, 'C1', N'C1 (8.5+)', 80, 100, 4);
            """);
    }

    private Task SeedGradedAttemptWithSkillScoresAsync(
        int userId,
        string certCode,
        int totalScore,
        string bandCode,
        int listeningScaled,
        int readingScaled,
        string startedAt) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');
            DECLARE @listSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @readSkill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({userId}, @exam, @cert, 'graded',
                    '{startedAt}', DATEADD(HOUR, 2, CAST('{startedAt}' AS DATETIMEOFFSET)),
                    DATEADD(HOUR, 1, CAST('{startedAt}' AS DATETIMEOFFSET)), {totalScore}, '{bandCode}');
            DECLARE @att INT = SCOPE_IDENTITY();

            INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore) VALUES
              (@att, @listSkill, @cert, 1, {listeningScaled}),
              (@att, @readSkill, @cert, 1, {readingScaled});
            """);

    [Fact]
    public async Task Simulator_for_new_learner_uses_scale_midpoints_and_middle_band_target()
    {
        var code = Unique("ST");
        await SeedToeicSimulatorCatalogAsync(code);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);
        var sim = dashboard.SimulatorConfig;

        Assert.NotNull(sim);
        Assert.Equal(990, sim.MaxTotalScore);
        Assert.Null(sim.CurrentTotalScore);
        Assert.Null(sim.CurrentBandCode);
        Assert.Equal("B1", sim.TargetBandCode);
        Assert.Equal(550, sim.TargetMinScore);

        Assert.Equal(new[] { "below_A1", "A1", "A2", "B1", "B2", "C1" }, sim.Bands.Select(b => b.Code));
        Assert.Equal(new[] { "LIST", "READ" }, sim.Skills.Select(s => s.SkillCode));
        Assert.All(sim.Skills, s =>
        {
            Assert.Equal(5, s.MinScaledScore);
            Assert.Equal(495, s.MaxScaledScore);
            Assert.Equal(5, s.Step);
            Assert.Equal(250, s.BaseScaledScore);
        });
    }

    [Fact]
    public async Task Simulator_seeds_baseline_scores_from_latest_graded_attempt_and_defaults_target_to_next_band()
    {
        var code = Unique("SG");
        await SeedToeicSimulatorCatalogAsync(code);
        var userId = await SeedLearnerAsync(code);

        await SeedGradedAttemptWithSkillScoresAsync(
            userId, code, totalScore: 450, bandCode: "A2", listeningScaled: 230, readingScaled: 220, startedAt: "2026-09-01T09:00:00+07:00");
        await SeedGradedAttemptWithSkillScoresAsync(
            userId, code, totalScore: 720, bandCode: "B1", listeningScaled: 380, readingScaled: 340, startedAt: "2026-09-10T09:00:00+07:00");

        var dashboard = await CreateService().GetDashboardAsync(userId);
        var sim = dashboard.SimulatorConfig;

        Assert.NotNull(sim);
        Assert.Equal(720, sim.CurrentTotalScore);
        Assert.Equal("B1", sim.CurrentBandCode);
        Assert.Equal("B2", sim.TargetBandCode);
        Assert.Equal(785, sim.TargetMinScore);

        var listening = Assert.Single(sim.Skills, s => s.SkillCode == "LIST");
        var reading = Assert.Single(sim.Skills, s => s.SkillCode == "READ");
        Assert.Equal(380, listening.BaseScaledScore);
        Assert.Equal(340, reading.BaseScaledScore);
    }

    [Fact]
    public async Task Simulator_uses_chosen_target_band_and_isolates_certificates_and_learners()
    {
        var toeic = Unique("S1");
        var vstep = Unique("S2");
        await SeedToeicSimulatorCatalogAsync(toeic);
        await SeedFourSkillNoScaleCatalogAsync(vstep);

        var userId = await SeedLearnerAsync(toeic);
        var otherUserId = await SeedLearnerAsync(toeic);

        // Other learner has high scores and C1 target on TOEIC; must not leak to userId.
        await SeedGradedAttemptWithSkillScoresAsync(
            otherUserId, toeic, totalScore: 950, bandCode: "C1", listeningScaled: 480, readingScaled: 470, startedAt: "2026-09-15T09:00:00+07:00");
        await database.SqlAsync($"""
            DECLARE @toeicId INT = (SELECT CertId FROM Certificates WHERE Code = '{toeic}');
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode) VALUES ({userId}, @toeicId, 'C1');
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode) VALUES ({otherUserId}, @toeicId, 'B2');
            """);

        var toeicDash = await CreateService().GetDashboardAsync(userId);
        var toeicSim = toeicDash.SimulatorConfig;

        Assert.NotNull(toeicSim);
        Assert.Equal(990, toeicSim.MaxTotalScore);
        Assert.Equal("C1", toeicSim.TargetBandCode);
        Assert.Equal(945, toeicSim.TargetMinScore);
        Assert.All(toeicSim.Skills, s => Assert.Equal(250, s.BaseScaledScore));

        // Switch to 4-skill certificate without ScoreScales rows: defaults to even division of MaxTotalScore (100 / 4 = 25).
        await CreateService().SwitchCertificateAsync(userId, vstep);
        var vstepDash = await CreateService().GetDashboardAsync(userId);
        var vstepSim = vstepDash.SimulatorConfig;

        Assert.NotNull(vstepSim);
        Assert.Equal(100, vstepSim.MaxTotalScore);
        Assert.Equal(new[] { "A2", "B1", "B2", "C1" }, vstepSim.Bands.Select(b => b.Code));
        Assert.Equal(new[] { "LIST", "READ", "WRIT", "SPEA" }, vstepSim.Skills.Select(s => s.SkillCode));
        Assert.All(vstepSim.Skills, s =>
        {
            Assert.Equal(10, s.MinScaledScore);
            Assert.Equal(25, s.MaxScaledScore);
            Assert.Equal(5, s.Step);
            Assert.Equal(15, s.BaseScaledScore);
        });
        Assert.Equal("B2", vstepSim.TargetBandCode);
        Assert.Equal(65, vstepSim.TargetMinScore);
    }
}

