using DataAccess;
using DataAccess.Scaffold;
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
            UPDATE ExamAttempts
            SET StartedAt = DATEADD(second, -1801, SYSDATETIMEOFFSET()),
                ExpiresAt = DATEADD(second, 1799, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attemptId};
            """);

    private async Task<ScoringFixture> SeedFixtureAsync(string connectionString, string certCode)
    {
        await RunSqlAsync(connectionString, $"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('{certCode}', N'{certCode}', 1,
              CASE '{certCode}' WHEN 'IELTS' THEN 'avg_half_step' WHEN 'VSTEP' THEN 'avg_whole' ELSE 'sum' END);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
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
