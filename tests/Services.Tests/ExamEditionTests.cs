using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #227 / D20: editions pin the paper a learner sat.
/// <para>
/// <b>What already existed, and is not re-tested here.</b> #157's
/// <c>ExamStructureLock.RequireEditableAsync</c> already refuses every structural mutation of a paper
/// with finished attempts (add/remove a group or question, move one, edit an option set or an answer
/// key) and already allows the cosmetic fields; <c>AdminExamPublishGateTests</c> pins both halves.
/// What this file adds is the part #157 did not have: <b>where the repair goes</b>, <b>what a fork
/// copies</b>, <b>which configuration an attempt is scored and reviewed against</b>, and the twelfth
/// door that went round the lock.
/// </para>
/// <para>
/// <b>The trap every test here is built around.</b> An edition that re-derives its timers, its band
/// names or its cut-score version at read time is not pinned: the fixture therefore changes the LIVE
/// configuration after the attempt is taken and asserts the attempt's rendered numbers did not move.
/// A test that only asserted "the edition row exists" would pass against an implementation that
/// stored nothing and read today's tables.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExamEditionTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    /// <summary><c>Certificates.Code</c> is <c>VARCHAR(20)</c>, so a certificate code is its own short
    /// helper rather than an exam name with the same generator.</summary>
    private static string CertCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..19];

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private AdminExamService CreateService(CompanionDbContext db) => new(db, TimeProvider.System);

    // ------------------------------------------------------------------ fixtures

    /// <summary>
    /// A two-skill certificate with two sections, each declaring two positions.
    /// <para>
    /// Both skills declare a duration because INV-5 sums the <em>served</em> skills' durations against the
    /// paper's — 20 + 40 = the 60 minutes every paper here declares.
    /// </para>
    /// </summary>
    private async Task<(int CertId, int FirstSkillId, int SecondSkillId, int FirstSectionId, int SecondSectionId)>
        SeedCertificateAsync(string code)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Edition cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes,
                                MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 20, 0, 100, 1);
            DECLARE @first INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes,
                                MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'GRAM', N'Grammar', 'reading', 2, 40, 0, 100, 1);
            DECLARE @second INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@first, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@second, @cert, 'reading', 'P6', N'Text Completion', 4, 2, 2);
            """);

        await using var db = Db();
        var cert = await db.Certificates.SingleAsync(c => c.Code == code);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var sections = await db.Sections.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        return (cert.CertId, skills[0].SkillId, skills[1].SkillId, sections[0].SectionId, sections[1].SectionId);
    }

    /// <summary>
    /// A two-skill paper with two positions in each section, published through the real publish door so
    /// the edition freeze runs. Returns the exam and its roster positions in sitting order.
    /// <para>
    /// <b>#272: authored on the pool, and that is now possible.</b> This fixture used to call
    /// <c>CreateQuestionGroupAsync</c>/<c>CreateQuestionAsync</c> because the readiness audit counted a
    /// section's positions from <c>Questions</c> alone: a pool-only paper reported "declares 1 item(s)
    /// but holds 0" and could not be published, so no edition could be frozen for these tests to
    /// inspect. #522315f made the audit and INV-1 read the roster, and the paper below clears all six
    /// invariants on the pool alone.
    /// </para>
    /// <para>
    /// INV-3 needs a human clearance naming each item, and ADM-02's trigger refuses one made by the
    /// batch's own uploader or by the item's last writer — so the fixture needs a second, independent
    /// reviewer. That is the rule working, not a setup inconvenience.
    /// </para>
    /// </summary>
    private async Task<(int ExamId, List<int> PositionIds)> SeedPublishedExamAsync(
        AdminExamService service, int certId, int firstSectionId, int secondSectionId, string name)
    {
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, name, 60, ExamPurposeDto.Mock, QuestionCount: 4));

        await using var db = Db();

        // A second account, because the first one uploads the batch the clearance is about.
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'reviewer')
              INSERT INTO Roles (Code, Name) VALUES ('reviewer', N'Reviewer');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'reviewer'), 'uploader-{Guid.NewGuid():N}@edition.local', 0x01, N'Uploader');
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'reviewer'), 'reviewer-{Guid.NewGuid():N}@edition.local', 0x01, N'Reviewer');
            DECLARE @rev INT = SCOPE_IDENTITY();
            INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
            VALUES ({certId}, N'edition-{Guid.NewGuid():N}.json', 'done', @u);
            """);

        var positions = new List<PoolFixture.Position>
        {
            await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, firstSectionId, "reading", "First skill one", displayOrder: 1),
            await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, firstSectionId, "reading", "First skill two", displayOrder: 2),
            await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, secondSectionId, "reading", "Second skill one", displayOrder: 1),
            await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, secondSectionId, "reading", "Second skill two", displayOrder: 2),
        };

        var batch = await db.ImportBatches.AsNoTracking()
            .Where(b => b.CertId == certId).OrderByDescending(b => b.BatchId).FirstAsync();
        var reviewerId = await db.Users.Where(u => u.Email.StartsWith("reviewer-")).OrderByDescending(u => u.UserId)
            .Select(u => u.UserId).FirstAsync();

        foreach (var position in positions)
        {
            db.ImportLogs.Add(new BusinessObjects.Entities.ImportLog
            {
                BatchId = batch.BatchId, Level = "info", Message = "accepted by the edition fixture",
                ItemId = position.ItemId, Attempt = 1,
            });
            db.ReviewSampleDecisions.Add(new BusinessObjects.Entities.ReviewSampleDecision
            {
                BatchId = batch.BatchId, ItemId = position.ItemId, ActorUserId = reviewerId,
                Decision = "clear", DecidedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        // Published through the service, so trg_Exams_PublishFreezesEdition writes the edition. The
        // six invariants are checked by the gate; this paper is an ordinary one and clears them.
        await service.PublishExamAsync(exam.ExamId);
        return (exam.ExamId, positions.Select(p => p.ExamItemId).ToList());
    }

    /// <summary>A learner id, created or reused, for an attempt.</summary>
    private async Task<int> LearnerAsync()
    {
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            """);

        await using var db = Db();
        var role = await db.Roles.SingleAsync(r => r.Code == "learner");
        var email = $"edition.{Guid.NewGuid():N}@local";
        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = email,
            PasswordHash = new byte[] { 1 },
            FullName = "Edition Learner",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>
    /// A finished attempt recorded directly, because the fixture wants a scored attempt without
    /// sitting the whole paper. The certificate's band grid is read by the caller's assertions, so the
    /// row's ScoreAggregationRule is written to a value the test can see change.
    /// </summary>
    private async Task<int> FinishedAttemptAsync(int userId, int examId, int certId, int totalScore, string bandCode)
    {
        await using var db = Db();
        var attempt = new BusinessObjects.Entities.ExamAttempt
        {
            UserId = userId,
            ExamId = examId,
            CertId = certId,
            Status = "graded",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-60),
            TotalScore = totalScore,
            BandCode = bandCode,
            WrittenByService = "AttemptEngine",
            ProjectionVersion = 1,
        };
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.AttemptId;
    }

    // ================================================================ criterion 1: the lock
    //
    // #157 already refuses all five structural mutations and already allows cosmetic ones; that is
    // pinned in AdminExamPublishGateTests. What #157 did NOT cover is a *shared* writer: PoolImportWriter
    // is called by two services, one of which guards and one of which happens to be safe only because
    // the exam it writes is brand new. The guard now lives in the writer, so the next caller cannot
    // reopen the hole.

    [Fact]
    public async Task Binding_an_item_to_a_paper_with_a_finished_attempt_is_refused()
    {
        var (certId, firstSkillId, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDBIND_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Bind "));
        var userId = await LearnerAsync();
        await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        var versionId = await SeedActiveVersionAsync(db, firstSkillId);
        var bindingsBefore = await db.ExamItems.CountAsync(b => b.ExamId == examId);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.BindItemAsync(examId, new BindPoolItemRequest(firstSectionId, versionId, 1)));

        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex.Code);
        Assert.Equal(bindingsBefore, await db.ExamItems.CountAsync(b => b.ExamId == examId));
    }

    /// <summary>A demo-tier active item with version 1, in the given skill's modality.</summary>
    private async Task<int> SeedActiveVersionAsync(CompanionDbContext db, int skillId)
    {
        var skill = await db.Skills.AsNoTracking().SingleAsync(s => s.SkillId == skillId);

        // The topic vocabulary is a closed set of twenty, so a canonical row is used rather than a
        // generated code - a synthetic one would be refused by trg_Topics_ClosedVocabulary.
        var code = skill.Modality == "reading" ? "R-DET" : "L-DET";
        var topic = await db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Modality == skill.Modality && t.Code == code);
        if (topic is null)
        {
            var seeded = new BusinessObjects.Entities.Topic
            {
                Modality = skill.Modality,
                Code = code,
                Name = code,
            };
            db.Topics.Add(seeded);
            await db.SaveChangesAsync();
            topic = await db.Topics.AsNoTracking()
                .SingleAsync(t => t.Modality == skill.Modality && t.Code == code);
        }

        var item = new BusinessObjects.Entities.Item
        {
            Modality = skill.Modality,
            TopicId = topic.TopicId,
            // CK_Items_Modality_Type: the item type is modality's own vocabulary, not "mcq".
            ItemType = skill.Modality == "reading" ? "sentence_cloze" : "short_exchange",
            CefrLevel = CefrLevel.B1,
            ContentTier = "demo",
            LifecycleStatus = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new BusinessObjects.Entities.ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = "Imported stem",
            OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
            KeyLabel = "A",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();
        return version.ItemVersionId;
    }

    // ================================================================ criterion 2: the fork

    [Fact]
    public async Task A_fork_copies_the_paper_and_leaves_the_original_locked()
    {
        var (certId, firstSkillId, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDFORK_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, sourcePositions) = await SeedPublishedExamAsync(
            service, certId, firstSectionId, secondSectionId, Unique("Forked "));
        var userId = await LearnerAsync();
        await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        var fork = await service.ForkEditionAsync(examId, new ForkExamEditionRequest(Unique("Edition 2 ")));

        // The fork is a complete paper: a paper row with no positions would satisfy "a new edition
        // exists" and be unsittable. A position is a roster row, so the roster is where completeness is
        // read - the three legacy tables hold nothing for either paper.
        Assert.NotEqual(examId, fork.ExamId);
        Assert.Equal(2, fork.EditionNumber);
        Assert.Equal(4, fork.BindingCount);
        Assert.Equal(4, await db.ExamItems.CountAsync(b => b.ExamId == fork.ExamId));
        Assert.Equal(
            await db.ExamItems.CountAsync(b => b.ExamId == examId),
            await db.ExamItems.CountAsync(b => b.ExamId == fork.ExamId));

        // The fork accepts the repair - a new position bound onto the same paper...
        var versionId = await SeedActiveVersionAsync(db, firstSkillId);
        var added = await service.BindItemAsync(fork.ExamId, new BindPoolItemRequest(firstSectionId, versionId, 3));
        Assert.Equal(3, added.DisplayOrder);

        // ...and the live edition still refuses it. Both directions matter: a fork that copied rows
        // but left the original editable would pass "the fork accepts the repair" alone.
        var refused = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.BindItemAsync(examId, new BindPoolItemRequest(firstSectionId, versionId, 3)));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, refused.Code);

        // And the source paper's own roster is untouched by the fork's edit.
        Assert.Equal(sourcePositions.Count, await db.ExamItems.CountAsync(b => b.ExamId == examId));
    }

    [Fact]
    public async Task A_fork_of_a_paper_with_no_finished_attempt_is_refused_because_in_place_is_cheaper()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDDRAFT_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Unsat "));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.ForkEditionAsync(examId, new ForkExamEditionRequest(Unique("Pointless "))));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("editable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fork_needs_its_own_name_because_the_name_is_unique_per_certificate()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDNAME_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Named "));
        var userId = await LearnerAsync();
        await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        var missing = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.ForkEditionAsync(examId, new ForkExamEditionRequest(null)));
        Assert.Equal(ApiError.Codes.Validation, missing.Code);

        var existing = await db.Exams.AsNoTracking().SingleAsync(e => e.ExamId == examId);
        var taken = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.ForkEditionAsync(examId, new ForkExamEditionRequest(existing.Name)));
        Assert.Equal(ApiError.Codes.Validation, taken.Code);
    }

    // ================================================================ criterion 3: the pin

    [Fact]
    public async Task An_attempt_records_the_edition_it_sat_and_a_fork_does_not_move_it()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDPIN_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Pinned "));
        var userId = await LearnerAsync();
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        // The edition the attempt names is the exam row it was created against, and the edition row
        // holds the frozen numbers. Both are facts, not guesses.
        var attempt = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal(examId, attempt.ExamId);

        var edition = await db.ExamEditions.AsNoTracking().SingleAsync(e => e.ExamId == examId);
        Assert.Equal(1, edition.EditionNumber);
        Assert.Equal(3600, edition.TotalDurationSeconds);
        Assert.Equal("sum", edition.ScoreAggregationRule);

        // A fork creates a SECOND edition; it does not re-point the attempt.
        var fork = await service.ForkEditionAsync(examId, new ForkExamEditionRequest(Unique("Pinned 2 ")));

        var afterFork = await db.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId);
        Assert.Equal(examId, afterFork.ExamId);
        Assert.NotEqual(fork.ExamId, afterFork.ExamId);
        Assert.NotNull(await db.ExamEditions.AsNoTracking().SingleOrDefaultAsync(e => e.ExamId == examId));
        Assert.Null(await db.ExamEditions.AsNoTracking().SingleOrDefaultAsync(e => e.ExamId == fork.ExamId));
    }

    /// <summary>
    /// The trap: an edition that re-derives its band name at read time is not pinned. The fixture
    /// renames the band AFTER the attempt and asserts the review still prints the frozen name.
    /// </summary>
    [Fact]
    public async Task A_review_prints_the_band_name_the_edition_froze_not_the_one_configured_since()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDBAND_"));
        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
            VALUES ({certId}, 'B1', N'Band as scored', 40, 79, 1),
                   ({certId}, 'B2', N'Higher band', 80, 100, 2);
            """);

        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Band "));
        // The edition was frozen when the paper published, so it must already hold the band grid.
        var edition = await db.ExamEditions.AsNoTracking().SingleAsync(e => e.ExamId == examId);
        Assert.Contains("Band as scored", edition.BandsJson, StringComparison.Ordinal);

        var userId = await LearnerAsync();
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        // Re-standardise the certificate after the attempt. The code on the attempt is unchanged and
        // so is the band the learner was awarded; only the NAME the page prints could drift.
        await database.SqlAsync($"UPDATE ScoreBands SET Name = N'Renamed after the fact' WHERE CertId = {certId} AND Code = 'B1';");

        var engine = new AttemptEngine(Db());
        var review = await engine.GetAttemptReviewAsync(attemptId, userId);

        Assert.Equal("B1", review.BandCode);
        Assert.Equal("Band as scored", review.BandName);
    }

    [Fact]
    public async Task A_finished_attempt_keeps_its_phase_split_when_a_skill_duration_is_edited()
    {
        var (certId, firstSkillId, secondSkillId, firstSectionId, secondSectionId) =
            await SeedCertificateAsync(CertCode("EDPHASE_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Phases "));
        var userId = await LearnerAsync();

        // A real attempt, created through the engine so its phases are built by the same code path a
        // learner's sitting uses. 20 min / 40 min weights over 3600 s gives 1200 s then 2400 s.
        var engine = new AttemptEngine(Db());
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        var before = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);
        Assert.NotNull(before.Phases);
        Assert.Equal(2, before.Phases!.Count);
        Assert.Equal(1200, before.Phases[0].DurationSeconds);
        Assert.Equal(2400, before.Phases[1].DurationSeconds);

        // The live configuration changes underneath the sitting. A pinned edition must ignore it.
        await database.SqlAsync($"""
            UPDATE Skills SET DurationMinutes = 50 WHERE SkillId = {firstSkillId};
            UPDATE Skills SET DurationMinutes = 10 WHERE SkillId = {secondSkillId};
            """);

        // A FRESH context: the first engine still tracks the Skill rows it loaded, so reusing it would
        // read the pre-edit values out of EF's identity map and the assertion would pass even with the
        // edition pin deleted. This is the mutation-proved half of the test.
        var after = await new AttemptEngine(Db()).GetAttemptTakeAsync(attempt.AttemptId, userId, null);
        Assert.Equal(1200, after.Phases![0].DurationSeconds);
        Assert.Equal(2400, after.Phases[1].DurationSeconds);
    }

    // ================================================================ criterion 4: mid-sitting

    [Fact]
    public async Task A_learner_mid_sitting_continues_on_the_edition_they_started_after_a_fork()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDMID_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Mid "));
        var userId = await LearnerAsync();

        // The fork is only reachable when the paper is locked, which needs a finished attempt. That is
        // also the real shape: a paper gets sat, a defect is found, and a repair is made while a second
        // learner is still sitting it.
        await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        var engine = new AttemptEngine(Db());
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        var before = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);

        // A fork happens while the sitting is in progress. Nothing about the sitting may move.
        var fork = await service.ForkEditionAsync(examId, new ForkExamEditionRequest(Unique("Mid 2 ")));
        // A pool paper's positions ARE roster rows, and the id a sitting addresses is the position's id
        // (#250) - so the fork's own ExamItemIds are what must not have leaked in.
        var forkPositionIds = await db.ExamItems.AsNoTracking()
            .Where(b => b.ExamId == fork.ExamId)
            .Select(b => b.ExamItemId)
            .ToListAsync();

        var after = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);

        Assert.Equal(before.TotalQuestions, after.TotalQuestions);
        Assert.Equal(examId, after.ExamId);
        Assert.Equal(
            before.Questions!.Select(q => q.QuestionId).ToList(),
            after.Questions!.Select(q => q.QuestionId).ToList());
        // The fork's positions are new rows, so their ids cannot have leaked into the sitting.
        Assert.Empty(after.Questions.Select(q => q.QuestionId).Intersect(forkPositionIds));
    }

    [Fact]
    public async Task A_learner_cannot_be_moved_onto_a_forked_edition()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDMOVE_"));
        await using var db = Db();
        var service = CreateService(db);

        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Move "));
        var userId = await LearnerAsync();

        // A finished attempt, so the paper is forkable, and it is the row under test: it has no child
        // rows, so the composite foreign key cannot refuse the move and the trigger is the only thing
        // that can. An attempt with answers is refused by FK_AttemptAnswers_AttemptExam first, which
        // would let this test pass with the trigger deleted.
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 60, "B1");
        var fork = await service.ForkEditionAsync(examId, new ForkExamEditionRequest(Unique("Move 2 ")));

        // The fork goes live, so trg_ExamAttempts_PublishedExamOnly cannot be the guard that answers
        // this UPDATE - otherwise the test would go green with the edition trigger deleted.
        await database.SqlAsync($"UPDATE Exams SET Status = 'published' WHERE ExamId = {fork.ExamId};");

        // criterion 5: an attempt cannot be edited into a different edition. `ExamId` IS the edition
        // reference, and both of its foreign keys admit a sibling edition of the same certificate, so
        // without the trigger this UPDATE would succeed and the review would render a paper the
        // learner never saw. The batch is executed directly rather than through
        // DatabaseFixture.SqlAsync, which turns a non-zero sqlcmd exit into a TrueException and would
        // hide which guard answered.
        var refused = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(async () =>
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new Microsoft.Data.SqlClient.SqlCommand(
                $"UPDATE ExamAttempts SET ExamId = {fork.ExamId} WHERE AttemptId = {attemptId};", connection);
            await command.ExecuteNonQueryAsync();
        });
        Assert.Contains("edition it sat", refused.Message, StringComparison.OrdinalIgnoreCase);

        await using var verify = Db();
        Assert.Equal(examId, (await verify.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == attemptId)).ExamId);
    }

    // ================================================================ criterion 5: the column

    [Fact]
    public async Task A_historical_attempt_with_no_edition_row_reads_todays_configuration_and_says_so()
    {
        var (certId, _, _, firstSectionId, secondSectionId) = await SeedCertificateAsync(CertCode("EDLEGACY_"));
        await database.SqlAsync($"""
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder)
            VALUES ({certId}, 'B1', N'Legacy band', 40, 79, 1);
            """);

        await using var db = Db();
        var service = CreateService(db);
        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Legacy "));

        // The edition is deleted to model a catalog that predates the table: every attempt taken
        // before #227 has no edition row, and "no edition" must be an explicit, readable state
        // rather than a NULL column that silently reads like "edition 0".
        await database.SqlAsync($"DELETE FROM ExamEditions WHERE ExamId = {examId};");

        var userId = await LearnerAsync();
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 60, "B1");

        var engine = new AttemptEngine(Db());
        var review = await engine.GetAttemptReviewAsync(attemptId, userId);

        // The review still works: it falls back to the live grid, which is the only grid that exists
        // for this attempt. That is a documented degradation, not a silent guess, and the row that
        // records it is the absence of the edition.
        Assert.Equal("Legacy band", review.BandName);
        Assert.Null(await db.ExamEditions.AsNoTracking().SingleOrDefaultAsync(e => e.ExamId == examId));
    }

    /// <summary>
    /// #257: the aggregation rule a re-score uses is the one the edition froze, not today's
    /// certificate column.
    /// <para>
    /// <b>The wrong input that would pass a weaker test:</b> asserting the total is simply non-zero, or
    /// asserting it equals what <see cref="AttemptEngine.AggregateTotalScore"/> returns for whatever
    /// rule the certificate happens to carry now. Both pass while the reader is consulting the live
    /// column, which is the defect: re-standardise a certificate after a sitting and its stored total
    /// silently means something else.
    /// </para>
    /// <para>
    /// Two skill scores of 40 and 60 make the two rules disagree by construction — <c>sum</c> is 100 and
    /// <c>avg_half_step</c> is 50 — so there is no value the test can accept from both.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_rescore_uses_the_rule_the_edition_froze_not_the_one_configured_since()
    {
        var (certId, firstSkillId, secondSkillId, firstSectionId, secondSectionId) =
            await SeedCertificateAsync(CertCode("EDRULE_"));

        await using var db = Db();
        var service = CreateService(db);
        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("Rule "));

        // The edition froze 'sum' at publish, because that is the certificate's rule at that moment.
        var edition = await db.ExamEditions.AsNoTracking().SingleAsync(e => e.ExamId == examId);
        Assert.Equal("sum", edition.ScoreAggregationRule, ignoreCase: true);

        var userId = await LearnerAsync();
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 0, bandCode: null);

        // Re-standardise the certificate afterwards. The attempt was sat under 'sum'; a re-score must
        // still use 'sum', or the learner's total changes meaning without the sitting changing.
        await database.SqlAsync($"UPDATE Certificates SET ScoreAggregationRule = 'avg_half_step' WHERE CertId = {certId};");

        var scores = new List<BusinessObjects.Entities.AttemptSkillScore>
        {
            new() { AttemptId = attemptId, SkillId = firstSkillId, CertId = certId, RawScore = 40, ScaledScore = 40 },
            new() { AttemptId = attemptId, SkillId = secondSkillId, CertId = certId, RawScore = 60, ScaledScore = 60 },
        };

        var (total, _) = await new AttemptEngine(Db()).WriteAttemptScoreAsync(attemptId, scores);

        // 100 is 'sum' (the frozen rule). 50 is 'avg_half_step' (today's certificate) and would mean the
        // reader consulted the column this ticket exists to stop it consulting.
        Assert.Equal(100, total);
    }

    /// <summary>
    /// The other half of #257: an attempt with <b>no</b> edition row keeps reading live configuration.
    /// #227 pinned that absence as the record ("no edition existed"), so this is intended behaviour and
    /// the fallback must survive the change above.
    /// <para>
    /// <b>The wrong input that would pass:</b> a test that only covers the frozen path, leaving the
    /// fallback to be discovered in production by a historical attempt.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_attempt_with_no_edition_row_rescores_under_todays_configuration()
    {
        var (certId, firstSkillId, secondSkillId, firstSectionId, secondSectionId) =
            await SeedCertificateAsync(CertCode("EDRULEL_"));

        await using var db = Db();
        var service = CreateService(db);
        var (examId, _) = await SeedPublishedExamAsync(service, certId, firstSectionId, secondSectionId, Unique("RuleL "));

        // A catalog that predates the table: every attempt taken before #227 has no edition row.
        await database.SqlAsync($"DELETE FROM ExamEditions WHERE ExamId = {examId};");

        var userId = await LearnerAsync();
        var attemptId = await FinishedAttemptAsync(userId, examId, certId, 0, bandCode: null);

        await database.SqlAsync($"UPDATE Certificates SET ScoreAggregationRule = 'avg_half_step' WHERE CertId = {certId};");

        var scores = new List<BusinessObjects.Entities.AttemptSkillScore>
        {
            new() { AttemptId = attemptId, SkillId = firstSkillId, CertId = certId, RawScore = 40, ScaledScore = 40 },
            new() { AttemptId = attemptId, SkillId = secondSkillId, CertId = certId, RawScore = 60, ScaledScore = 60 },
        };

        var (total, _) = await new AttemptEngine(Db()).WriteAttemptScoreAsync(attemptId, scores);

        // No edition means no frozen rule, so today's certificate decides. 50, not 100.
        Assert.Equal(50, total);
    }
}
