using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Services;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AttemptEngineMultiCertScoringTests(DatabaseFixture database)
{
    private sealed record ScoringFixture(
        int CertId,
        int ExamId,
        int ListeningSkillId,
        int ReadingSkillId,
        int ListeningQuestionId,
        int ReadingQuestionId,
        int ListeningCorrectOptionId,
        int ReadingCorrectOptionId);

    private static async Task RunSqlAsync(string connectionString, string commandText)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    // Both fixture skills have no duration weight, so a 60-minute attempt splits evenly.
    private static Task StartReadingPhaseAsync(string connectionString, int attemptId) =>
        RunSqlAsync(connectionString, $"""
            UPDATE a
            SET a.StartedAt = DATEADD(second, -1801, SYSDATETIMEOFFSET()),
                a.ExpiresAt = DATEADD(second, 1799, SYSDATETIMEOFFSET()),
                a.ActiveSkillId = (
                    SELECT TOP 1 s.SkillId
                    FROM Skills s
                    JOIN Sections sec ON sec.SkillId = s.SkillId
                    JOIN Questions q ON q.SectionId = sec.SectionId
                    WHERE q.ExamId = a.ExamId AND s.SkillId <> a.ActiveSkillId
                    ORDER BY s.DisplayOrder
                ),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

    private async Task<ScoringFixture> SeedFixtureAsync(string connectionString, string certCode)
    {
        // E1.1 (#146): the scale is the skill's own data now, so the fixture declares it explicitly.
        var scale = certCode switch
        {
            "TOEIC" => (Min: 5, Max: 495, Step: 5),
            "IELTS" => (Min: 10, Max: 90, Step: 5),
            _ => (Min: 10, Max: 100, Step: 1)
        };

        await RunSqlAsync(connectionString, $"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('{certCode}', N'{certCode}', 1,
              CASE '{certCode}' WHEN 'IELTS' THEN 'avg_half_step' WHEN 'VSTEP' THEN 'avg_whole' ELSE 'sum' END);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, {scale.Min}, {scale.Max}, {scale.Step}),
              (@cert, 'READ', N'Reading', 'reading', 2, {scale.Min}, {scale.Max}, {scale.Step});
            DECLARE @listening INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @reading INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@listening, @cert, 'listening', 'L1', N'Listening', 1),
              (@reading, @cert, 'reading', 'R1', N'Reading', 2);
            DECLARE @listeningSection INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'L1');
            DECLARE @readingSection INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'R1');

            IF '{certCode}' = 'TOEIC'
              INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
                (@cert, 'LOW', N'Low', 0, 899, 1), (@cert, 'HIGH', N'High', 900, 990, 2);
            ELSE IF '{certCode}' = 'IELTS'
              INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
                (@cert, 'LOW', N'Low', 10, 59, 1), (@cert, 'HIGH', N'High', 60, 90, 2);
            ELSE
              INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
                (@cert, 'LOW', N'Low', 10, 49, 1), (@cert, 'HIGH', N'High', 50, 100, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'{certCode} scoring exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES
              (@exam, @cert, @listeningSection, 1),
              (@exam, @cert, @readingSection, 2);
            DECLARE @listeningGroup INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @listeningSection);
            DECLARE @readingGroup INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @readingSection);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@listeningGroup, @exam, @listeningSection, @listening, N'Listening question', 1, 'mcq', 1),
              (@readingGroup, @exam, @readingSection, @reading, N'Reading question', 1, 'mcq', 2);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
            SELECT QuestionId, 'mcq', 'A', N'Correct', 1 FROM Questions WHERE ExamId = @exam;
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
            SELECT QuestionId, 'mcq', 'B', N'Incorrect', 0 FROM Questions WHERE ExamId = @exam;

            """);

        await using var db = DbContextFactory.Create(connectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).ToDictionaryAsync(s => s.Code);
        var questions = await db.Questions
            .Where(q => q.ExamId == exam.ExamId)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .ToDictionaryAsync(q => q.SkillId);

        return new ScoringFixture(
            cert.CertId,
            exam.ExamId,
            skills["LIST"].SkillId,
            skills["READ"].SkillId,
            questions[skills["LIST"].SkillId].QuestionId,
            questions[skills["READ"].SkillId].QuestionId,
            questions[skills["LIST"].SkillId].QuestionOptionQuestionNavigations.Single(o => o.IsCorrect).OptionId,
            questions[skills["READ"].SkillId].QuestionOptionQuestionNavigations.Single(o => o.IsCorrect).OptionId);
    }

    [Fact]
    public async Task Scales_And_Aggregates_Scores_By_Certificate()
    {
        var databaseName = $"AttemptScoring_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            var userEmail = $"{Guid.NewGuid():N}@test.local";
            var fixtures = new Dictionary<string, ScoringFixture>
            {
                ["TOEIC"] = await SeedFixtureAsync(connectionString, "TOEIC"),
                ["IELTS"] = await SeedFixtureAsync(connectionString, "IELTS"),
                ["VSTEP"] = await SeedFixtureAsync(connectionString, "VSTEP")
            };

            await RunSqlAsync(connectionString, $"""
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Test Learner');
                """);
            int userId;
            await using (var userDb = DbContextFactory.Create(connectionString))
            {
                userId = await userDb.Users.Where(u => u.Email == userEmail).Select(u => u.UserId).SingleAsync();
            }

            var expected = new Dictionary<string, (int Fallback, int Total, string Band)>
            {
                ["TOEIC"] = (250, 900, "HIGH"),
                ["IELTS"] = (50, 65, "HIGH"),
                ["VSTEP"] = (55, 56, "HIGH")
            };

            foreach (var (certCode, fixture) in fixtures)
            {
                await using var engineDb = DbContextFactory.Create(connectionString);
                var engine = new AttemptEngine(engineDb);
                var expectedScore = expected[certCode];

                Assert.Equal(
                    expectedScore.Fallback,
                    await engine.ComputeScaledScoreAsync(fixture.CertId, fixture.ListeningSkillId, fixture.ExamId, 1, 2));

                var listeningScore = certCode switch { "TOEIC" => 450, "IELTS" => 60, _ => 50 };
                var readingScore = certCode switch { "TOEIC" => 450, "IELTS" => 65, _ => 61 };
                await RunSqlAsync(connectionString, $"""
                    INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
                      ({fixture.CertId}, {fixture.ListeningSkillId}, {fixture.ExamId}, 1, {listeningScore}),
                      ({fixture.CertId}, {fixture.ReadingSkillId}, {fixture.ExamId}, 1, {readingScore});
                    """);

                var attempt = await engine.CreateAttemptAsync(userId, fixture.CertId, fixture.ExamId, 60);
                await engine.SaveAnswerAsync(attempt.AttemptId, userId, fixture.ListeningQuestionId, fixture.ListeningCorrectOptionId, false);
                await StartReadingPhaseAsync(connectionString, attempt.AttemptId);
                await using var readingDb = DbContextFactory.Create(connectionString);
                var readingEngine = new AttemptEngine(readingDb);
                await readingEngine.SaveAnswerAsync(attempt.AttemptId, userId, fixture.ReadingQuestionId, fixture.ReadingCorrectOptionId, false);

                var result = await readingEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

                Assert.Equal(expectedScore.Total, result.TotalScore);
                Assert.Equal(expectedScore.Band, result.BandCode);
                Assert.Equal(2, result.SkillScores.Count);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Scales_Follow_Each_Skills_Own_Step_And_Interpolate_Between_Rows()
    {
        var databaseName = $"AttemptScoringStep_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            // Two skills on the same certificate, different scales: 0-50 step 1 and 0-200 step 10.
            await RunSqlAsync(connectionString, """
                INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
                VALUES ('STEP', N'STEP', 1, 'sum');
                DECLARE @cert INT = SCOPE_IDENTITY();
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
                  (@cert, 'FIFTY', N'Fifty', 'listening', 1, 0, 50, 1),
                  (@cert, 'TWOHT', N'Two hundred', 'reading', 2, 0, 200, 10);
                DECLARE @fifty INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'FIFTY');
                DECLARE @twoht INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'TWOHT');
                -- Rows bracket the midpoint at 22.5 (FIFTY) and 90 (TWOHT), so raw 5 of 10 interpolates to 25 and 100.
                INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
                  (@cert, @fifty, NULL, 0, 0), (@cert, @fifty, NULL, 10, 50),
                  (@cert, @twoht, NULL, 0, 0), (@cert, @twoht, NULL, 10, 200);
                """);

            int certId, fiftyId, twohtId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                certId = await db.Certificates.Where(c => c.Code == "STEP").Select(c => c.CertId).SingleAsync();
                fiftyId = await db.Skills.Where(s => s.CertId == certId && s.Code == "FIFTY").Select(s => s.SkillId).SingleAsync();
                twohtId = await db.Skills.Where(s => s.CertId == certId && s.Code == "TWOHT").Select(s => s.SkillId).SingleAsync();
            }

            await using var engineDb = DbContextFactory.Create(connectionString);
            var engine = new AttemptEngine(engineDb);

            // 50% raw -> inside 20-30, a multiple of 1
            var fifty = await engine.ComputeScaledScoreAsync(certId, fiftyId, 0, 5, 10);
            Assert.InRange(fifty, 20, 30);
            Assert.Equal(25, fifty);

            // Same 50% raw on 0-200 step 10 -> a multiple of 10 around 100
            var twoht = await engine.ComputeScaledScoreAsync(certId, twohtId, 0, 5, 10);
            Assert.Equal(0, twoht % 10);
            Assert.InRange(twoht, 95, 105);
            Assert.Equal(100, twoht);

            // Interpolation, not nearest row: raw 3 of 10 gives 15 and 60, which is not a seeded row.
            Assert.Equal(15, await engine.ComputeScaledScoreAsync(certId, fiftyId, 0, 3, 10));
            Assert.Equal(60, await engine.ComputeScaledScoreAsync(certId, twohtId, 0, 3, 10));

            // 7 of 10 on the step-10 skill is 140 exactly; on step 1 it is 35.
            Assert.Equal(35, await engine.ComputeScaledScoreAsync(certId, fiftyId, 0, 7, 10));
            Assert.Equal(140, await engine.ComputeScaledScoreAsync(certId, twohtId, 0, 7, 10));
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Resolves_Score_Bands_With_Gaps_To_Highest_Threshold_Met()
    {
        var databaseName = $"AttemptScoringGap_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            var userEmail = $"{Guid.NewGuid():N}@test.local";
            var fixture = await SeedFixtureAsync(connectionString, "VSTEP");

            // Re-seed bands with explicit gaps: A2 (40-45), B1 (50-60), C1 (80-100)
            await RunSqlAsync(connectionString, $"""
                DELETE FROM ScoreBands WHERE CertId = {fixture.CertId};
                INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
                  ({fixture.CertId}, 'A2', N'A2', 40, 45, 1),
                  ({fixture.CertId}, 'B1', N'B1', 50, 60, 2),
                  ({fixture.CertId}, 'C1', N'C1', 80, 100, 3);
                """);

            await RunSqlAsync(connectionString, $"""
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Gap Learner');
                """);
            int userId;
            await using (var userDb = DbContextFactory.Create(connectionString))
            {
                userId = await userDb.Users.Where(u => u.Email == userEmail).Select(u => u.UserId).SingleAsync();
            }

            // Intermediate score 48 falls between A2 (max 45) and B1 (min 50)
            await RunSqlAsync(connectionString, $"""
                INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
                  ({fixture.CertId}, {fixture.ListeningSkillId}, {fixture.ExamId}, 1, 48),
                  ({fixture.CertId}, {fixture.ReadingSkillId}, {fixture.ExamId}, 1, 48);
                """);

            await using var engineDb = DbContextFactory.Create(connectionString);
            var engine = new AttemptEngine(engineDb);
            var attempt = await engine.CreateAttemptAsync(userId, fixture.CertId, fixture.ExamId, 60);
            await engine.SaveAnswerAsync(attempt.AttemptId, userId, fixture.ListeningQuestionId, fixture.ListeningCorrectOptionId, false);
            await StartReadingPhaseAsync(connectionString, attempt.AttemptId);
            await using var readingDb = DbContextFactory.Create(connectionString);
            var readingEngine = new AttemptEngine(readingDb);
            await readingEngine.SaveAnswerAsync(attempt.AttemptId, userId, fixture.ReadingQuestionId, fixture.ReadingCorrectOptionId, false);

            var result = await readingEngine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

            Assert.Equal(48, result.TotalScore);
            Assert.Equal("A2", result.BandCode);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
