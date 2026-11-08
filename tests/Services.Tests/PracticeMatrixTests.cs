using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #238 / SCR-PRAC-01: the objective practice matrix.
/// <para>
/// The matrix is a <b>read</b>: it draws nothing (so it is not a second selection path, which #235
/// deleted two of) and writes nothing (ADR-0012). These tests pin the two facts the hub renders:
/// four-state mastery derived from real answers, and the reopen instant of a resting cell — using the
/// same answer rows and the same formulas the selector and the path already use.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PracticeMatrixTests(DatabaseFixture database)
{
    private const string BatchPrefix = "matrixtest";

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private sealed record Cell(int CertId, int TopicId, CefrLevel Level, int ItemId, int ItemVersionId);

    /// <summary>One certificate with a reading skill, one reading topic and one active A1 item.
    /// <para>
    /// The level is A1 deliberately: the twenty-topic vocabulary is shared by the whole suite, and every
    /// other pool fixture seeds B1 or B2. A cell at A1 therefore belongs to this class for the run, so a
    /// neighbour's active item cannot keep the cell open and make a resting assertion pass by accident.
    /// </para>
    /// </summary>
    private async Task<Cell> SeedCellAsync(string certCode, string topicCode = "R-COH")
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Matrix Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RMT', N'Reading', 'reading', 1, 60);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = '{topicCode}')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', '{topicCode}', N'Matrix topic');
            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = '{topicCode}');

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'A1', 'sentence_cloze', 'production', 'active', '{BatchPrefix}-{certCode}');
            DECLARE @item INT = (SELECT ItemId FROM Items WHERE BatchTag = '{BatchPrefix}-{certCode}');

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'Matrix stem.', N'a', N'b', N'c', N'd', 'A', N'Because.');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var topic = await db.Topics.SingleAsync(t => t.Modality == "reading" && t.Code == topicCode);
        var version = await db.ItemVersions.SingleAsync(v => v.Item.BatchTag == $"{BatchPrefix}-{certCode}");

        return new Cell(cert.CertId, topic.TopicId, CefrLevel.A1, version.ItemId, version.ItemVersionId);
    }

    private async Task<int> SeedLearnerAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Matrix Learner',
                    (SELECT CertId FROM Certificates WHERE Code = '{certCode}'));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private PracticeMatrixService CreateMatrix(TimeProvider? clock = null) =>
        new(DbContextFactory.Create(database.ConnectionString), clock);

    /// <summary>
    /// The wrong input a weaker test would pass is a matrix that colours every cell the same. Three
    /// answers in one cell, all wrong, must read as Learning — and the B2 cell of the same topic, with
    /// no answers, must read as Untested rather than inheriting its neighbour's state.
    /// </summary>
    [Fact]
    public async Task Mastery_is_derived_per_cell_from_the_answers_given_in_it()
    {
        var certCode = Unique("MAT");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Two wrong answers in the B1 cell and nothing anywhere else. One answer per session: the
        // schema holds one answer per item version per session, so two sessions is the honest shape.
        for (var i = 1; i <= 2; i++)
        {
            await using var db = DbContextFactory.Create(database.ConnectionString);
            var session = new BusinessObjects.Entities.PracticeSession
            {
                UserId = userId, CertId = cell.CertId, TopicId = cell.TopicId, CefrLevel = cell.Level,
                Modality = "reading", QuestionCount = 1, Status = "finished", CorrectCount = 0,
                QuestionSource = "all", StartedAt = DateTimeOffset.UtcNow,
            };
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            db.PracticeAnswers.Add(new BusinessObjects.Entities.PracticeAnswer
            {
                SessionId = session.SessionId, ItemId = cell.ItemId, ItemVersionId = cell.ItemVersionId,
                SelectedLabel = "B", IsCorrect = false, DisplayOrder = 1, AnsweredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var matrix = CreateMatrix();
        var cells = await matrix.GetMatrixAsync(userId, cell.CertId);

        var a1 = Assert.Single(cells, c => c.TopicCode == "R-COH" && c.LevelCode == "A1");
        Assert.Equal(Contracts.PracticeCellMasteryDto.Learning, a1.Mastery);

        // A level of the same topic that holds this learner's answers cannot also be Untested: mastery
        // is per cell, so one level's state never colours another's. (Other levels may hold a
        // neighbour's stock, so the assertion is about this learner's answers, not the cell's emptiness.)
        var otherLevels = cells.Where(c => c.TopicCode == "R-COH" && c.LevelCode != "A1").ToList();
        Assert.All(otherLevels, c => Assert.NotEqual(Contracts.PracticeCellMasteryDto.Learning, c.Mastery));
    }

    /// <summary>
    /// A cell whose only item was just answered wrongly is resting: the wrong-answer cooldown is 24
    /// hours, and the matrix must say when it reopens rather than only that it is shut. The wrong input
    /// a weaker test would pass is a cell marked resting with a null reopen time — a dimmed plate with
    /// no time reads as deleted.
    /// </summary>
    [Fact]
    public async Task A_cell_whose_every_item_is_cooling_reports_when_it_reopens()
    {
        var certCode = Unique("MAT");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        var answeredAt = DateTime.UtcNow;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var session = new BusinessObjects.Entities.PracticeSession
            {
                UserId = userId, CertId = cell.CertId, TopicId = cell.TopicId, CefrLevel = cell.Level,
                Modality = "reading", QuestionCount = 1, Status = "finished", CorrectCount = 0,
                QuestionSource = "all", StartedAt = DateTimeOffset.UtcNow,
            };
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            db.PracticeAnswers.Add(new BusinessObjects.Entities.PracticeAnswer
            {
                SessionId = session.SessionId, ItemId = cell.ItemId, ItemVersionId = cell.ItemVersionId,
                SelectedLabel = "B", IsCorrect = false, DisplayOrder = 1, AnsweredAt = answeredAt,
            });
            await db.SaveChangesAsync();
        }

        var now = new DateTimeOffset(DateTime.SpecifyKind(answeredAt, DateTimeKind.Utc));
        var matrix = CreateMatrix(new FixedClock(now));
        var cells = await matrix.GetMatrixAsync(userId, cell.CertId);

        var a1 = Assert.Single(cells, c => c.TopicCode == "R-COH" && c.LevelCode == "A1");
        Assert.True(a1.IsResting, "a cell whose only item was just answered wrongly must be resting");
        Assert.NotNull(a1.ReopensAt);

        // The stored column is a legacy DATETIME2, so it keeps ~microseconds; the instant is exact to
        // the second, which is the resolution the page prints.
        var expected = now + MasterySchedule.WrongAnswerCooldown;
        Assert.Equal(expected.ToUnixTimeSeconds(), a1.ReopensAt!.Value.ToUnixTimeSeconds());
    }

    /// <summary>
    /// A cell with no answers is open, not resting, and reads Untested. The wrong input that would pass
    /// a weaker test is a matrix that treats "never answered" as "cooling".
    /// </summary>
    [Fact]
    public async Task An_unanswered_cell_is_open_and_untested()
    {
        var certCode = Unique("MAT");
        var cell = await SeedCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        var matrix = CreateMatrix();
        var cells = await matrix.GetMatrixAsync(userId, cell.CertId);

        var a1 = Assert.Single(cells, c => c.TopicCode == "R-COH" && c.LevelCode == "A1");
        Assert.False(a1.IsResting);
        Assert.Null(a1.ReopensAt);
        Assert.Equal(Contracts.PracticeCellMasteryDto.Untested, a1.Mastery);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
