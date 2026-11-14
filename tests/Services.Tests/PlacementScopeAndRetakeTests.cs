using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services.Tests;

/// <summary>
/// #289: placement scope (<see cref="PlacementScope.AllSkills"/> vs
/// <see cref="PlacementScope.ListeningAndReading"/>) and paid retake rules.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementScopeAndRetakeTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EE289_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PlacementScopeAndRetakeTests(DatabaseFixture database) => _database = database;

    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static string WriteBatch(string batch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ee289-{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, batch);
        return path;
    }

    private Task SqlAsync(string batch) =>
        _database.SqlCmdFileToDatabaseAsync(_dbName, WriteBatch(batch));

    private sealed record FourSkillFixture(
        string CertCode,
        int CertId,
        int ExamId,
        IReadOnlyDictionary<string, int> SkillIds,
        IReadOnlyDictionary<int, int> PositionIds,
        IReadOnlyDictionary<int, int> CorrectOptionBySkillId);

    private async Task<FourSkillFixture> SeedFourSkillCertAsync(string code, bool listeningRequireAudio = true)
    {
        var lines = new List<string>
        {
            $"INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);",
            "DECLARE @cert INT = SCOPE_IDENTITY();",
            "INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)",
            "  VALUES (@cert, N'Placement 4-Skill', 'published', 30, 'placement', 90);",
        };

        var skills = new (string Code, string Modality, bool FreeResponse, bool RequireAudio)[]
        {
            ("LIST", "listening", false, listeningRequireAudio),
            ("READ", "reading", false, false),
            ("SPEAK", "speaking", true, false),
            ("WRIT", "writing", true, false),
        };

        var index = 0;
        foreach (var (skillCode, modality, freeResponse, requireAudio) in skills)
        {
            index++;
            lines.Add($"""
                INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
                  VALUES (@cert, '{skillCode}', N'{skillCode}', '{modality}', {index}, 0, 100, 1);
                INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder, RequireAudio)
                  VALUES ((SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = '{skillCode}'),
                          @cert, '{modality}', 'S{index}', N'Section {index}', {(freeResponse ? 2 : 1)}, {index}, {(requireAudio ? 1 : 0)});
                """);
        }

        lines.Add("""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1), (@cert, 'B1', N'Intermediate', 201, 500, 2);

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 0, 0 FROM Skills WHERE CertId = @cert;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 1, 250 FROM Skills WHERE CertId = @cert;
            """);

        await SqlAsync(string.Join("\n", lines));

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var skillIds = await db.Skills.Where(s => s.CertId == certId).ToDictionaryAsync(s => s.Code, s => s.SkillId);

        var positionIds = new Dictionary<int, int>();
        var correctOptionBySkillId = new Dictionary<int, int>();
        foreach (var (skillCode, modality, freeResponse, _) in skills)
        {
            var sectionId = await db.Sections
                .Where(s => s.CertId == certId && s.SkillId == skillIds[skillCode])
                .Select(s => s.SectionId).SingleAsync();

            if (freeResponse)
            {
                var first = await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
                await PoolFixture.AddProductiveAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}b", displayOrder: 2);
                positionIds[skillIds[skillCode]] = first.ExamItemId;
            }
            else
            {
                var position = await PoolFixture.AddMcqAsync(
                    db, examId, certId, sectionId, modality, $"Q{skillCode}", displayOrder: 1);
                positionIds[skillIds[skillCode]] = position.ExamItemId;
                correctOptionBySkillId[skillIds[skillCode]] =
                    await PoolFixture.OptionIdAsync(db, examId, position.ExamItemId, "A");
            }
        }

        return new FourSkillFixture(code, certId, examId, skillIds, positionIds, correctOptionBySkillId);
    }

    private async Task<int> SeedLearnerAsync(string certCode, int credits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Learner', @cert, SYSDATETIMEOFFSET());
            """);

        await using var db = Db();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (credits > 0)
        {
            var adminId = await db.Users.Where(u => u.Email == "seed-admin@test.local").Select(u => (int?)u.UserId).FirstOrDefaultAsync();
            if (adminId is null)
            {
                await SqlAsync("""
                    INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                    VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'seed-admin@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                    """);
                adminId = await db.Users.Where(u => u.Email == "seed-admin@test.local").Select(u => u.UserId).SingleAsync();
            }

            db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId,
                Reason = CreditReason.AdminGrant.ToCode(),
                Delta = credits,
                AdminUserId = adminId,
                Note = "Seed test credits",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return userId;
    }

    [Fact]
    public async Task ListeningAndReading_static_fallback_omits_speaking_and_writing_from_paper_and_mic_preflight()
    {
        var f = await SeedFourSkillCertAsync(Unique("LR"));
        var userId = await SeedLearnerAsync(f.CertCode);

        await using var db = Db();
        var engine = new AttemptEngine(db);

        var attempt = await engine.CreateAttemptAsync(
            userId, f.CertId, f.ExamId, 30, PlacementScope.ListeningAndReading);

        Assert.Equal(PlacementScope.ListeningAndReading.ToCode(), attempt.PlacementScope);

        var preflight = await engine.GetPreflightStatusAsync(attempt.AttemptId, userId);
        Assert.True(preflight.RequiresAudio);
        Assert.False(preflight.RequiresMicrophone);

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);

        Assert.Equal(2, take.TotalQuestions);
        Assert.All(take.Questions, q =>
            Assert.True(q.Modality is Modality.Listening or Modality.Reading));
        Assert.Empty(await db.FreeResponses.Where(fr => fr.AttemptId == attempt.AttemptId).ToListAsync());
    }

    [Fact]
    public async Task ListeningAndReading_result_writer_omits_speaking_and_writing()
    {
        var f = await SeedFourSkillCertAsync(Unique("LRW"));
        var userId = await SeedLearnerAsync(f.CertCode);

        await using var db = Db();
        var engine = new AttemptEngine(db);
        var attempt = await engine.CreateAttemptAsync(
            userId, f.CertId, f.ExamId, 30, PlacementScope.ListeningAndReading);

        var placement = new PlacementTest
        {
            UserId = userId,
            CertId = f.CertId,
            ExamId = f.ExamId,
            AttemptId = attempt.AttemptId,
            Status = PlacementStatus.InProgress.ToCode(),
            Scope = PlacementScope.ListeningAndReading.ToCode(),
            TakenAt = DateTimeOffset.UtcNow,
        };
        db.PlacementTests.Add(placement);
        await db.SaveChangesAsync();

        await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, f.PositionIds[f.SkillIds["LIST"]],
            f.CorrectOptionBySkillId[f.SkillIds["LIST"]], false);
        var toRead = await engine.TransitionToNextSkillAsync(attempt.AttemptId, userId);
        Assert.Equal(f.SkillIds["READ"], toRead.ActiveSkillId);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, f.PositionIds[f.SkillIds["READ"]],
            f.CorrectOptionBySkillId[f.SkillIds["READ"]], false);

        // Next transition finishes Listening & Reading directly (does not route into Speaking/Writing).
        var finished = await engine.TransitionToNextSkillAsync(attempt.AttemptId, userId);
        Assert.Equal(AttemptStatus.Graded, finished.Status);

        var updated = await db.PlacementTests.AsNoTracking()
            .SingleAsync(p => p.PlacementId == placement.PlacementId);

        Assert.NotNull(updated.ListeningPct);
        Assert.NotNull(updated.ReadingPct);
        Assert.Null(updated.SpeakingPct);
        Assert.Null(updated.WritingPct);
    }

    private static PlacementService Service(
        CompanionDbContext db,
        TimeProvider? clock = null,
        PlacementOptions? options = null) =>
        new(db, new LearnerService(db), attemptEngine: null, clock: clock, options: options);

    private static async Task SitAndFinishAsync(
        CompanionDbContext db,
        PlacementService svc,
        FourSkillFixture f,
        int userId,
        int placementId,
        int attemptId)
    {
        var engine = new AttemptEngine(db);
        await engine.VerifyPreflightAsync(attemptId, userId);
        await engine.SaveAnswerAsync(attemptId, userId, f.PositionIds[f.SkillIds["LIST"]],
            f.CorrectOptionBySkillId[f.SkillIds["LIST"]], false);
        await svc.SubmitPlacementTestAsync(userId, placementId, isAutoSubmit: false);
    }

    private async Task<(string CertCode, int ExamId, List<int> WritItemIds)> SeedWritingOnlyCertAsync(string code)
    {
        await SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Cert {code}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
              VALUES (@cert, N'Placement Writing Only', 'published', 30, 'placement', 2);
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
              VALUES (@cert, 'WRIT', N'Writing', 'writing', 1, 0, 100, 1);
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder, RequireAudio)
              VALUES ((SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT'),
                      @cert, 'writing', 'S1', N'Writing', 2, 1, 0);
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A2', N'Elementary', 0, 200, 1), (@cert, 'B1', N'Intermediate', 201, 500, 2);

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 0, 0 FROM Skills WHERE CertId = @cert;
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
              SELECT @cert, SkillId, NULL, 1, 250 FROM Skills WHERE CertId = @cert;
            """);

        await using var db = Db();
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();
        var sectionId = await db.Sections.Where(s => s.CertId == certId).Select(s => s.SectionId).SingleAsync();
        var q1 = await PoolFixture.AddProductiveAsync(db, examId, certId, sectionId, "writing", "QW1", displayOrder: 1);
        var q2 = await PoolFixture.AddProductiveAsync(db, examId, certId, sectionId, "writing", "QW2", displayOrder: 2);
        return (code, examId, [q1.ExamItemId, q2.ExamItemId]);
    }

    [Fact]
    public async Task First_placement_per_cert_is_free_and_writes_no_ledger_row_even_with_zero_balance()
    {
        var f = await SeedFourSkillCertAsync(Unique("FREE"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 0);

        await using var db = Db();
        var svc = Service(db);

        var preflight = await svc.GetPreflightAsync(userId);
        Assert.False(preflight.IsRetake);
        Assert.Null(preflight.CreditBalance);

        var catalog = await svc.GetPlacementCatalogAsync(userId);
        Assert.False(catalog.IsRetake);
        Assert.Null(catalog.CreditBalance);

        var started = await svc.StartPlacementTestAsync(
            userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);

        Assert.Equal(PlacementScope.ListeningAndReading, started.Scope);
        Assert.Equal(0, started.CreditsCharged);

        var row = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == started.PlacementId);
        Assert.Equal(PlacementScope.ListeningAndReading.ToCode(), row.Scope);
        Assert.Null(row.CreditTxnId);
        Assert.Equal(0, await db.CreditTransactions.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Retake_charges_configured_default_price_by_scope_and_links_ledger_row()
    {
        var f = await SeedFourSkillCertAsync(Unique("PRC"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 10);
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(
                userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            await SitAndFinishAsync(db, svc, f, userId, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var preflight = await svc.GetPreflightAsync(userId);
            Assert.True(preflight.IsRetake);
            Assert.Equal(5, preflight.AllSkillsRetakeCredits);
            Assert.Equal(3, preflight.ListeningAndReadingRetakeCredits);
            Assert.Equal(10, preflight.CreditBalance);

            var catalog = await svc.GetPlacementCatalogAsync(userId);
            Assert.True(catalog.IsRetake);
            Assert.Equal(5, catalog.AllSkillsRetakeCredits);
            Assert.Equal(3, catalog.ListeningAndReadingRetakeCredits);
            Assert.Equal(10, catalog.CreditBalance);

            var retake4 = await svc.StartPlacementTestAsync(
                userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            Assert.Equal(PlacementScope.AllSkills, retake4.Scope);
            Assert.Equal(5, retake4.CreditsCharged);

            var row4 = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == retake4.PlacementId);
            Assert.NotNull(row4.CreditTxnId);
            var txn4 = await db.CreditTransactions.AsNoTracking().SingleAsync(t => t.TxnId == row4.CreditTxnId);
            Assert.Equal(CreditReason.PlacementRetake.ToCode(), txn4.Reason);
            Assert.Equal(-5, txn4.Delta);
            Assert.Contains("all 4 skills", txn4.Note, StringComparison.OrdinalIgnoreCase);
            Assert.Null(txn4.AdminUserId);
            Assert.Null(txn4.OrderId);
            Assert.Null(txn4.AttemptId);

            await SitAndFinishAsync(db, svc, f, userId, retake4.PlacementId, retake4.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var retake2 = await svc.StartPlacementTestAsync(
                userId, f.ExamId, PlacementReuseChoice.ReuseValid, PlacementScope.ListeningAndReading);
            Assert.Equal(PlacementScope.ListeningAndReading, retake2.Scope);
            Assert.Equal(3, retake2.CreditsCharged);

            var row2 = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == retake2.PlacementId);
            var txn2 = await db.CreditTransactions.AsNoTracking().SingleAsync(t => t.TxnId == row2.CreditTxnId);
            Assert.Equal(-3, txn2.Delta);
            Assert.Contains("Listening & Reading", txn2.Note, StringComparison.OrdinalIgnoreCase);

            var balance = await new CreditLedger(db).GetBalanceAsync(userId);
            Assert.Equal(2, balance);
        }
    }

    [Fact]
    public async Task Retake_price_uses_configured_PlacementOptions_without_code_changes()
    {
        var f = await SeedFourSkillCertAsync(Unique("OPT"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 20);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var opts = new PlacementOptions { AllSkillsRetakeCredits = 9, ListeningAndReadingRetakeCredits = 4 };

        await using (var db = Db())
        {
            var svc = Service(db, clock, opts);
            var first = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            await SitAndFinishAsync(db, svc, f, userId, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        await using (var db = Db())
        {
            var svc = Service(db, clock, opts);
            var preflight = await svc.GetPreflightAsync(userId);
            Assert.Equal(9, preflight.AllSkillsRetakeCredits);
            Assert.Equal(4, preflight.ListeningAndReadingRetakeCredits);

            var retake = await svc.StartPlacementTestAsync(
                userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
            Assert.Equal(4, retake.CreditsCharged);
            Assert.Equal(16, await new CreditLedger(db).GetBalanceAsync(userId));
        }
    }

    [Fact]
    public async Task Retake_with_insufficient_credits_is_blocked_before_creating_sitting_or_abandoning_prior_sitting()
    {
        var f = await SeedFourSkillCertAsync(Unique("INSUF"));
        int secondExamId;
        await using (var setup = Db())
        {
            var e2 = new Exam
            {
                CertId = f.CertId,
                Name = "Placement Secondary",
                Status = ExamStatus.Published.ToCode(),
                DurationMinutes = 30,
                Purpose = ExamPurpose.Placement.ToCode(),
                QuestionCount = 10,
            };
            setup.Exams.Add(e2);
            await setup.SaveChangesAsync();
            secondExamId = e2.ExamId;
        }

        var userId = await SeedLearnerAsync(f.CertCode, credits: 7);
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        int activePlacementId;
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            await SitAndFinishAsync(db, svc, f, userId, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var retake = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            activePlacementId = retake.PlacementId;
        }

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var ex = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.StartPlacementTestAsync(userId, secondExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading));

            Assert.Equal(PlacementException.InsufficientCredits, ex.Code);

            // Original active sitting must NOT be abandoned, and no new sitting or ledger row was created.
            var placements = await db.PlacementTests.AsNoTracking().Where(p => p.UserId == userId && p.Status == PlacementStatus.InProgress.ToCode()).ToListAsync();
            Assert.Single(placements);
            Assert.Equal(activePlacementId, placements[0].PlacementId);
            Assert.Equal(PlacementStatus.InProgress.ToCode(), placements[0].Status);
            Assert.Equal(2, await db.ExamAttempts.CountAsync(a => a.UserId == userId));
            Assert.Equal(2, await new CreditLedger(db).GetBalanceAsync(userId));
        }
    }

    [Fact]
    public async Task Abandoned_paid_retake_keeps_its_charge_and_resuming_in_progress_retake_does_not_double_charge()
    {
        var f = await SeedFourSkillCertAsync(Unique("RESAB"));
        int secondExamId;
        await using (var setup = Db())
        {
            var e2 = new Exam
            {
                CertId = f.CertId,
                Name = "Placement B",
                Status = ExamStatus.Published.ToCode(),
                DurationMinutes = 30,
                Purpose = ExamPurpose.Placement.ToCode(),
                QuestionCount = 10,
            };
            setup.Exams.Add(e2);
            await setup.SaveChangesAsync();
            secondExamId = e2.ExamId;
        }

        var userId = await SeedLearnerAsync(f.CertCode, credits: 10);
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            await SitAndFinishAsync(db, svc, f, userId, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        int paidSittingId;
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var retake = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            paidSittingId = retake.PlacementId;
            Assert.Equal(5, retake.CreditsCharged);

            // Resume same in-progress sitting -> returns same sitting, zero extra charge.
            var resumed = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            Assert.Equal(paidSittingId, resumed.PlacementId);
            Assert.Equal(0, resumed.CreditsCharged);
            Assert.Equal(5, await new CreditLedger(db).GetBalanceAsync(userId));

            // Starting another exam abandons the paid sitting, and its charge stands; the new sitting charges 3.
            var secondRetake = await svc.StartPlacementTestAsync(userId, secondExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
            Assert.Equal(3, secondRetake.CreditsCharged);

            var abandonedRow = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == paidSittingId);
            Assert.Equal(PlacementStatus.Abandoned.ToCode(), abandonedRow.Status);
            Assert.NotNull(abandonedRow.CreditTxnId);
            Assert.Equal(2, await new CreditLedger(db).GetBalanceAsync(userId));
        }
    }

    [Fact]
    public async Task IdempotencyKey_prevents_double_charge_on_repeated_retake_confirmation()
    {
        var f = await SeedFourSkillCertAsync(Unique("IDEM"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 15);
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            await SitAndFinishAsync(db, svc, f, userId, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(25));

        var key = $"retake-{Guid.NewGuid():N}";
        await using var db1 = Db();
        await using var db2 = Db();
        var svc1 = Service(db1, clock);
        var svc2 = Service(db2, clock);

        var t1 = svc1.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills, key);
        var t2 = svc2.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills, key);
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(results[0].PlacementId, results[1].PlacementId);

        // Repeat with the same key after completion also replays cleanly without charging again.
        await using var db3 = Db();
        var replay = await Service(db3, clock).StartPlacementTestAsync(
            userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills, key);
        Assert.Equal(results[0].PlacementId, replay.PlacementId);
        Assert.Equal(5, replay.CreditsCharged);

        Assert.Equal(1, await db3.CreditTransactions.CountAsync(
            t => t.UserId == userId && t.Reason == CreditReason.PlacementRetake.ToCode()));
        Assert.Equal(10, await new CreditLedger(db3).GetBalanceAsync(userId));
    }

    [Fact]
    public async Task Retake_cooldown_blocks_within_24h_and_waives_only_on_grading_failure_not_skip_or_timeout()
    {
        var f = await SeedFourSkillCertAsync(Unique("CDW"));
        var clock = new MutableClock(DateTimeOffset.UtcNow);

        // 1. Finished placement < 24h ago blocks paid retake without charging.
        var userDone = await SeedLearnerAsync(f.CertCode, credits: 20);
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userDone, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
            await SitAndFinishAsync(db, svc, f, userDone, first.PlacementId, first.AttemptId);
        }

        clock.Advance(TimeSpan.FromHours(2));
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var blocked = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.StartPlacementTestAsync(userDone, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading));
            Assert.Equal(PlacementException.PlacementCooldown, blocked.Code);
            Assert.Equal(20, await new CreditLedger(db).GetBalanceAsync(userDone));
        }

        // 2. Candidate skip (< 24h) does NOT waive cooldown.
        var userSkip = await SeedLearnerAsync(f.CertCode, credits: 20);
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userSkip, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
            await new AttemptEngine(db).VerifyPreflightAsync(first.AttemptId, userSkip);
            var abandonedEx = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.SubmitPlacementTestAsync(userSkip, first.PlacementId, isAutoSubmit: false));
            Assert.Equal(PlacementException.PlacementAbandoned, abandonedEx.Code);
            var blocked = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.StartPlacementTestAsync(userSkip, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading));
            Assert.Equal(PlacementException.PlacementCooldown, blocked.Code);
            Assert.Equal(20, await new CreditLedger(db).GetBalanceAsync(userSkip));
        }

        // 3. Inactivity timeout (< 24h) does NOT waive cooldown.
        var userTimeout = await SeedLearnerAsync(f.CertCode, credits: 20);
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userTimeout, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);
            await new AttemptEngine(db).VerifyPreflightAsync(first.AttemptId, userTimeout);
            var abandonedEx = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.SubmitPlacementTestAsync(userTimeout, first.PlacementId, isAutoSubmit: true));
            Assert.Equal(PlacementException.PlacementAbandoned, abandonedEx.Code);
            var blocked = await Assert.ThrowsAsync<PlacementException>(() =>
                svc.StartPlacementTestAsync(userTimeout, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading));
            Assert.Equal(PlacementException.PlacementCooldown, blocked.Code);
            Assert.Equal(20, await new CreditLedger(db).GetBalanceAsync(userTimeout));
        }

        // 4. AiGradingFailed (< 24h, every skill failed grading) WAIVES cooldown, allowing paid retake.
        var wf = await SeedWritingOnlyCertAsync(Unique("CDWFAIL"));
        var userFail = await SeedLearnerAsync(wf.CertCode, credits: 20);
        int failPlacementId;
        int failAttemptId;
        await using (var db = Db())
        {
            var svc = Service(db, clock);
            var first = await svc.StartPlacementTestAsync(userFail, wf.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            failPlacementId = first.PlacementId;
            failAttemptId = first.AttemptId;
            await new AttemptEngine(db).VerifyPreflightAsync(failAttemptId, userFail);
            foreach (var itemId in wf.WritItemIds)
            {
                await svc.SaveDraftAsync(userFail, failPlacementId, itemId, "A written placement response.");
            }
            await svc.SubmitPlacementTestAsync(userFail, failPlacementId, isAutoSubmit: false);
        }

        await SqlAsync($"""
            UPDATE AiGradings SET Status = 'failed', CompletedAt = SYSDATETIMEOFFSET(), FailureReason = N'Model outage'
              WHERE FreeResponseId IN (SELECT FreeResponseId FROM FreeResponses WHERE AttemptId = {failAttemptId});
            UPDATE FreeResponses SET Status = 'graded' WHERE AttemptId = {failAttemptId};
            """);

        await using (var db = Db())
        {
            await new AttemptEngine(db).UpdatePlacementTestOutcomeAsync(failPlacementId);
            var svc = Service(db, clock);
            var waivedRetake = await svc.StartPlacementTestAsync(
                userFail, wf.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
            Assert.Equal(5, waivedRetake.CreditsCharged);
            Assert.Equal(15, await new CreditLedger(db).GetBalanceAsync(userFail));
        }
    }

    [Fact]
    public async Task Two_skill_preflight_status_omits_microphone_requirement()
    {
        var f = await SeedFourSkillCertAsync(Unique("PFMIC"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 10);

        await using var db = Db();
        var svc = Service(db);
        var started = await svc.StartPlacementTestAsync(
            userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.ListeningAndReading);

        var status = await svc.GetPlacementPreflightStatusAsync(userId, started.PlacementId);
        Assert.False(status.RequiresMicrophone);
        Assert.True(status.RequiresAudio);
    }

    [Fact]
    public async Task First_placement_is_free_even_when_an_earlier_in_progress_sitting_exists_for_another_exam()
    {
        var f = await SeedFourSkillCertAsync(Unique("IPFREE"));
        var userId = await SeedLearnerAsync(f.CertCode, credits: 10);

        await using var db = Db();
        var firstExam = await db.Exams.SingleAsync(e => e.ExamId == f.ExamId);
        var secondExam = new Exam
        {
            CertId = firstExam.CertId,
            Name = $"{firstExam.Name} B",
            Purpose = ExamPurpose.Placement.ToCode(),
            Status = ExamStatus.Published.ToCode(),
            DurationMinutes = firstExam.DurationMinutes,
            QuestionCount = firstExam.QuestionCount,
        };
        db.Exams.Add(secondExam);
        await db.SaveChangesAsync();

        var svc = Service(db);
        var first = await svc.StartPlacementTestAsync(userId, f.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
        Assert.Equal(0, first.CreditsCharged);

        var second = await svc.StartPlacementTestAsync(userId, secondExam.ExamId, PlacementReuseChoice.Remeasure, PlacementScope.AllSkills);
        Assert.Equal(0, second.CreditsCharged);
        Assert.Equal(0, await db.CreditTransactions.CountAsync(
            t => t.UserId == userId && t.Reason == CreditReason.PlacementRetake.ToCode()));
        Assert.Equal(10, await new CreditLedger(db).GetBalanceAsync(userId));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _now = utcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
