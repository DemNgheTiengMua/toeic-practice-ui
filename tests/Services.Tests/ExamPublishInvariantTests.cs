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
/// #226 / ADM-12: the six publish invariants, and the door that will not open without them.
/// <para>
/// <b>One fixture per violation, and each fixture violates exactly one.</b> A fixture that broke two
/// invariants would pass on a gate that only checked the other one, so every test below asserts that
/// its own invariant failed <em>and</em> that the five it did not break still passed. That is the
/// assertion a shared "broken paper" fixture cannot make.
/// </para>
/// <para>
/// These run against real SQL Server with the real schema, because the gate reads facts triggers and
/// services already wrote (a clearance row, a rubric lock, a stratum decision) instead of re-deriving
/// them.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExamPublishInvariantTests(DatabaseFixture database)
{
    private AdminExamService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    /// <summary>
    /// The app's real DbContext stack, with the acting account #201's interceptor stamps. Publishing in
    /// a STRICT catalog needs it: <c>trg_Exams_TriageCannotWrite</c> refuses a write that declares
    /// nobody, so a context without an acting account cannot publish there at all - which is the guard
    /// working, not a setup problem. The fixture catalog exempts it, which is why most tests here can
    /// use the plain context.
    /// </summary>
    private AdminExamService CreateServiceAs(int userId)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new StaffAccountStampInterceptor(new FixedActingAccount(userId)))
            .Options;
        return new AdminExamService(new CompanionDbContext(options), TimeProvider.System);
    }

    private sealed class FixedActingAccount(int userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    /// <summary>
    /// Runs a batch that must be rejected, over a raw connection. <see cref="DatabaseFixture.SqlAsync"/>
    /// turns a non-zero sqlcmd exit into an assertion failure, so a trigger violation would look like a
    /// broken fixture rather than the behaviour under test - and its <c>SqlException</c> number, which is
    /// how a test tells one guard from another, would be lost.
    /// </summary>
    private async Task<Microsoft.Data.SqlClient.SqlException> ExecExpectingFailureAsync(string batch)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Microsoft.Data.SqlClient.SqlCommand(batch, connection);
        return await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private async Task<int> SeedStaffAccountAsync()
    {
        var email = $"{Guid.NewGuid():N}@staff.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Admin');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    /// <summary>
    /// The baseline every test edits one thing from. A certificate with one reading skill at 60 minutes,
    /// one section declaring one question, a draft exam declaring 60 minutes, one rendered question, and
    /// one cleared pool item bound at that position.
    /// <para>
    /// The pool item is bound at position 1 because that is where the sitting renders it
    /// (<c>AttemptEngine.PlaceBindings</c> maps a binding onto the legacy question at its position), so
    /// the baseline is a paper the learner would actually sit - not one with a binding in thin air.
    /// </para>
    /// </summary>
    private async Task<(int CertId, int SkillId, int SectionId, int ExamId, int ItemId)> SeedBaselineAsync(
        string certCode)
    {
        var tag = Unique("B");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            -- The canonical topics, guarded row by row. The schema declares the closed set but does not
            -- seed it; seed-demo.sql does. A test that inserted an invented code would be refused by
            -- trg_Topics_ClosedVocabulary, which is the trigger working.
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Explicit factual detail and extraction');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'writing' AND Code = 'W-ARG')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('writing', 'W-ARG', N'Argumentation and position defence');

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes,
                                MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 60, 5, 495, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Paper {certCode}', 'draft', 60, 'mock', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- The rendered position the sitting will serve. A binding supplies this position's CONTENT.
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Choose the word.', 'B1', 'mcq', 1);
            DECLARE @q INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q, 'mcq', 'A', N'correct', 1),
              (@q, 'mcq', 'B', N'wrong', 0);

            -- The uploader who sent the batch, and the DIFFERENT reviewer who clears it. ADM-02's trigger
            -- refuses a decision whose actor uploaded or last modified the batch, so one account cannot
            -- do both - which is the invariant, not a fixture inconvenience.
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'reviewer')
              INSERT INTO Roles (Code, Name) VALUES ('reviewer', N'Reviewer');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'reviewer'), '{tag}@test.local', 0x01, N'Uploader');
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'reviewer'), '{tag}-rev@test.local', 0x01, N'Reviewer');
            DECLARE @rev INT = SCOPE_IDENTITY();

            -- The batch the item arrived in, the stratum that audited its cell, and the clearance.
            INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
            VALUES (@cert, N'{tag}.json', 'done', @u);
            DECLARE @b INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId);

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', N'{tag}');
            DECLARE @item INT = SCOPE_IDENTITY();

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'Choose the word.', N'correct', N'wrong', N'other', N'another', 'A', N'Because it is.');

            -- ImportLog is the only record of which item a record became.
            INSERT INTO ImportLog (BatchId, RowNo, Level, Message, ItemId, Attempt)
            VALUES (@b, 1, 'info', N'Accepted.', @item, 1);

            -- A decided stratum needs a stamp (CK_ReviewStrata_DecisionIsStamped), and only
            -- StratifiedAuditService may write one (trg_ReviewStrata_SingleWriter).
            INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize, Status,
                                      DecidedAt, WrittenByService, ProjectionVersion)
            VALUES (@b, @topic, 'B1', 1, 1, 'approved', SYSDATETIMEOFFSET(), 'StratifiedAuditService', 1);

            INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision)
            VALUES (@b, @item, @rev, 'clear');

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT @exam, @sec, @cert, v.ItemVersionId, @item, 'reading', 1, 1
            FROM ItemVersions v WHERE v.ItemId = @item;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skill = await db.Skills.SingleAsync(s => s.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var item = await db.Items.SingleAsync(i => i.BatchTag == tag);
        return (cert.CertId, skill.SkillId, section.SectionId, exam.ExamId, item.ItemId);
    }

    private static ExamInvariantDto Invariant(ExamAuditResultDto audit, string code) =>
        audit.Invariants!.Single(i => i.Code == code);

    private static void AssertOnlyFailed(ExamAuditResultDto audit, params string[] codes)
    {
        foreach (var invariant in audit.Invariants!)
        {
            var expectedToFail = codes.Contains(invariant.Code);
            Assert.True(
                expectedToFail == !invariant.Satisfied,
                $"invariant {invariant.Code} reported Satisfied={invariant.Satisfied}. "
                + $"Deficits: {string.Join(" | ", audit.Issues.Where(i => i.Code == invariant.Code).Select(i => i.Message))}");
        }
    }

    // ---------------------------------------------------------------- the six are six, always

    [Fact]
    public async Task The_audit_reports_all_six_invariants_and_the_baseline_passes_every_one()
    {
        var (_, _, _, examId, _) = await SeedBaselineAsync(Unique("BASE"));

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);

        // Six rows, named. A gate that returned only its failures could not show a pass, and a rail
        // that painted "all satisfied" from an empty list would be right by accident.
        Assert.Equal(ExamInvariants.All, audit.Invariants!.Select(i => i.Code));
        Assert.True(
            audit.CanPublish,
            string.Join(" | ", audit.Issues.Select(i => $"{i.Code}: {i.Message}")));
        AssertOnlyFailed(audit);
    }

    // ---------------------------------------------------------------- INV-1: skill with no section

    [Fact]
    public async Task An_enabled_skill_with_no_section_fails_inv1_and_names_the_skill()
    {
        var (certId, _, _, examId, _) = await SeedBaselineAsync(Unique("INV1A"));

        // A second enabled skill with no section at all. This is the half nothing checked: the audit
        // counted questions per section and never asked whether a skill had been given one.
        await database.SqlAsync($"""
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES ({certId}, 'LST', N'Listening', 'listening', 2, 45);
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv1 = Invariant(audit, ExamInvariants.SkillCoverage);

        Assert.False(inv1.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.SkillCoverage),
            i => i.Message.Contains("LST", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        // The other five still pass, so this fixture is about coverage alone.
        AssertOnlyFailed(audit, ExamInvariants.SkillCoverage);
    }

    // ---------------------------------------------------------------- INV-1: section count mismatch

    [Fact]
    public async Task A_section_holding_a_different_number_than_it_declares_fails_inv1_and_names_the_section()
    {
        var (_, _, sectionId, examId, _) = await SeedBaselineAsync(Unique("INV1B"));

        // The blueprint now promises three where the paper renders one. Under is a hole in the paper;
        // the sitting allocates minutes per the blueprint, so a learner would reach the end early.
        await database.SqlAsync($"UPDATE Sections SET ExpectedQuestionCount = 3 WHERE SectionId = {sectionId};");

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv1 = Invariant(audit, ExamInvariants.SkillCoverage);

        Assert.False(inv1.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.SkillCoverage),
            i => i.SectionId == sectionId && i.Message.Contains("2 short", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        AssertOnlyFailed(audit, ExamInvariants.SkillCoverage);
    }

    // ---------------------------------------------------------------- INV-2: stimulus group

    [Fact]
    public async Task A_bound_item_whose_stimulus_children_overlap_fails_inv2_through_the_gates_own_rule()
    {
        var (certId, skillId, sectionId, examId, _) = await SeedBaselineAsync(Unique("INV2"));

        // Two children of ONE stimulus citing the SAME evidence span: intersection over union is 1.0,
        // far above CNT-12's 0.15. The assertion is that the publish gate CALLS gate four - a second
        // copy would drift, and this would then pass on the copy.
        await database.SqlAsync($"""
            -- A section with two rendered positions, so both children have somewhere to sit.
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES ({skillId}, {certId}, 'reading', 'P6', N'Text completion', 4, 2, 2);
            DECLARE @sec2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES ({examId}, {certId}, @sec2, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder) VALUES
              (@grp, {examId}, @sec2, {skillId}, N'First rendered.', 'B1', 'mcq', 1),
              (@grp, {examId}, @sec2, {skillId}, N'Second rendered.', 'B1', 'mcq', 2);

            INSERT INTO Stimuli (CefrLevel, Passage, DisplayOrder)
            VALUES ('B1', REPLICATE(CAST(N'word ' AS NVARCHAR(MAX)), 100), 1);
            DECLARE @stim INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId);

            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
            VALUES (@stim, 'reading', @topic, 'B1', 'passage_comprehension', 'demo', 'active');
            DECLARE @i1 INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel,
                                      EvidenceStart, EvidenceEnd, EvidenceUnit)
            VALUES (@i1, 1, N'First.', N'a', N'b', N'c', N'd', 'A', 10, 60, 'char');

            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
            VALUES (@stim, 'reading', @topic, 'B1', 'passage_comprehension', 'demo', 'active');
            DECLARE @i2 INT = SCOPE_IDENTITY();
            -- The identical span: two items measuring one sentence twice.
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel,
                                      EvidenceStart, EvidenceEnd, EvidenceUnit)
            VALUES (@i2, 1, N'Second.', N'a', N'b', N'c', N'd', 'A', 10, 60, 'char');

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT {examId}, @sec2, {certId}, v.ItemVersionId, @i1, 'reading', 1, 1 FROM ItemVersions v WHERE v.ItemId = @i1;
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT {examId}, @sec2, {certId}, v.ItemVersionId, @i2, 'reading', 2, 1 FROM ItemVersions v WHERE v.ItemId = @i2;

            -- Clear both, so INV-3 does not fail for an unrelated reason and mask this one. The reviewer
            -- must not be the uploader: ADM-02's trigger refuses a self-approval.
            DECLARE @b INT = (SELECT TOP 1 BatchId FROM ImportBatches WHERE CertId = {certId});
            DECLARE @rev INT = (SELECT TOP 1 UserId FROM Users WHERE Email LIKE '%rev@test.local' ORDER BY UserId);
            INSERT INTO ImportLog (BatchId, RowNo, Level, Message, ItemId, Attempt) VALUES
              (@b, 2, 'info', N'ok', @i1, 1), (@b, 3, 'info', N'ok', @i2, 1);
            INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision) VALUES
              (@b, @i1, @rev, 'clear'), (@b, @i2, @rev, 'clear');
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv2 = Invariant(audit, ExamInvariants.StimulusGroup);

        Assert.False(inv2.Satisfied);
        // The refusal is gate four's own sentence - it names the IoU rule, the 0.15 ceiling and the
        // measured overlap. A re-implementation would phrase it differently, so this asserts the shared
        // arithmetic ran rather than a copy of it.
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.StimulusGroup),
            i => i.Message.Contains("CNT-12", StringComparison.Ordinal)
                 && i.Message.Contains("overlap by 1.00", StringComparison.Ordinal)
                 && i.Message.Contains("0.15", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        AssertOnlyFailed(audit, ExamInvariants.StimulusGroup);
    }

    // ---------------------------------------------------------------- INV-3: not active

    [Fact]
    public async Task A_bound_item_that_is_not_active_fails_inv3_and_names_the_item()
    {
        var (_, _, _, examId, itemId) = await SeedBaselineAsync(Unique("INV3A"));

        // `quarantined` is the reachable not-served state (CNT-42 names active -> quarantined; a
        // hand-set `validated` would be an unnamed transition and the lifecycle trigger would refuse
        // it, which would make this fixture about that guard instead of this one). Every pool reader
        // filters on `active`, so binding this would put a hole on the paper.
        await database.SqlAsync($"UPDATE Items SET LifecycleStatus = 'quarantined', LastWrittenBy = NULL WHERE ItemId = {itemId};");

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv3 = Invariant(audit, ExamInvariants.ItemClearance);

        Assert.False(inv3.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.ItemClearance),
            i => i.Message.Contains($"item {itemId}", StringComparison.Ordinal)
                 && i.Message.Contains("not 'active'", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        AssertOnlyFailed(audit, ExamInvariants.ItemClearance);
    }

    // ---------------------------------------------------------------- INV-3: the no-stratum case

    /// <summary>
    /// <b>The input that would pass a lazy guard while being wrong.</b> An <c>active</c> item that
    /// arrived in NO batch: hand-authored, or seeded. It has no stratum, so
    /// <c>strata.All(approved)</c> over an empty collection returns <c>true</c> and the item sails
    /// through - the exact hole the ticket predicts. An item nobody audited is not audited.
    /// </summary>
    [Fact]
    public async Task An_active_item_that_arrived_in_no_batch_fails_inv3_rather_than_passing_on_an_empty_all()
    {
        var (certId, _, sectionId, examId, _) = await SeedBaselineAsync(Unique("INV3B"));

        await database.SqlAsync($"""
            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId);
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', NULL);
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (@item, 1, N'Unbatched stem.', N'a', N'b', N'c', N'd', 'A');
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT {examId}, {sectionId}, {certId}, v.ItemVersionId, @item, 'reading', 2, 1
            FROM ItemVersions v WHERE v.ItemId = @item;
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv3 = Invariant(audit, ExamInvariants.ItemClearance);

        Assert.False(inv3.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.ItemClearance),
            i => i.Message.Contains("arrived in no batch", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);
    }

    // ---------------------------------------------------------------- INV-3: pending stratum

    /// <summary>
    /// A stratum that was drawn and never decided. The item is active and arrived in a batch, so the
    /// only thing wrong is that the audit is still `pending` - which must not read as cleared.
    /// <para>
    /// Its own fixture rather than the baseline plus an edit: the baseline's strata are approved and it
    /// carries a `clear` decision, and a decision is append-only (`trg_ReviewSampleDecisions_NoSelfApproval`
    /// refuses DELETE outright), so the baseline cannot be walked back to "undecided".
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_item_whose_stratum_is_still_pending_fails_inv3_and_names_the_status()
    {
        var certCode = Unique("INV3C");
        var tag = Unique("P");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 60);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Pending {certCode}', 'draft', 60, 'mock', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Choose the word.', 'B1', 'mcq', 1);

            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'reviewer')
              INSERT INTO Roles (Code, Name) VALUES ('reviewer', N'Reviewer');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'reviewer'), '{tag}@test.local', 0x01, N'Uploader');
            DECLARE @u INT = SCOPE_IDENTITY();

            INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
            VALUES (@cert, N'{tag}.json', 'done', @u);
            DECLARE @b INT = SCOPE_IDENTITY();

            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId);
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'demo', 'active', N'{tag}');
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (@item, 1, N'Choose the word.', N'a', N'b', N'c', N'd', 'A');
            INSERT INTO ImportLog (BatchId, RowNo, Level, Message, ItemId, Attempt)
            VALUES (@b, 1, 'info', N'Accepted.', @item, 1);

            -- Drawn and never decided. No clear decision exists, so nothing says a human cleared it.
            INSERT INTO ReviewStrata (BatchId, TopicId, CefrLevel, ItemCount, SampleSize, Status)
            VALUES (@b, @topic, 'B1', 1, 1, 'pending');

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT @exam, @sec, @cert, v.ItemVersionId, @item, 'reading', 1, 1
            FROM ItemVersions v WHERE v.ItemId = @item;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var examId = await db.Exams.Where(e => e.CertId == db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).Single())
            .Select(e => e.ExamId).SingleAsync();

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv3 = Invariant(audit, ExamInvariants.ItemClearance);

        Assert.False(inv3.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.ItemClearance),
            i => i.Message.Contains("pending", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        // Only INV-3: every other invariant holds, so this is about the undecided cell alone.
        AssertOnlyFailed(audit, ExamInvariants.ItemClearance);
    }

    // ---------------------------------------------------------------- INV-4: no rubric lock

    [Fact]
    public async Task An_enabled_writing_skill_with_no_locked_rubric_fails_inv4_and_names_the_skill()
    {
        var (certId, _, _, examId, _) = await SeedBaselineAsync(Unique("INV4A"));

        await database.SqlAsync($"""
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES ({certId}, 'WRIT', N'Writing', 'writing', 2, 60);
            DECLARE @w INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@w, {certId}, 'writing', 'W1', N'Essay', 4, 1, 2);
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv4 = Invariant(audit, ExamInvariants.RubricReady);

        Assert.False(inv4.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.RubricReady),
            i => i.Message.Contains("WRIT", StringComparison.Ordinal)
                 && i.Message.Contains("no locked rubric", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);
    }

    // ---------------------------------------------------------------- INV-4: missing anchors

    [Fact]
    public async Task A_locked_rubric_whose_criteria_lack_one_anchor_fails_inv4_and_names_the_criterion_and_band()
    {
        var (certId, _, _, examId, _) = await SeedBaselineAsync(Unique("INV4B"));

        // A complete 4x7 writing rubric, locked with an ACCEPTED ladder - so the lock and its trigger
        // are both satisfied - but one criterion is short its band-4 anchor. The lock trigger checks
        // shape and the ladder, not the anchors, so this is the link the ticket says is missing.
        await database.SqlAsync($"""
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES ({certId}, 'WRIT', N'Writing', 'writing', 2, 60);
            DECLARE @w INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@w, {certId}, 'writing', 'W1', N'Essay', 4, 1, 2);

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, RubricDescription, MaxScore, Weight, DisplayOrder) VALUES
              ({certId}, @w, 'W_TR', N'Task', N'x', 6, 1.0, 1),
              ({certId}, @w, 'W_CC', N'Coherence', N'x', 6, 1.0, 2),
              ({certId}, @w, 'W_LR', N'Lexis', N'x', 6, 1.0, 3),
              ({certId}, @w, 'W_GR', N'Grammar', N'x', 6, 1.0, 4);

            INSERT INTO GradingCriterionDescriptors (CriterionId, Band, Descriptor, BinaryGates)
              SELECT c.CriterionId, b.Band,
                     N'Band ' + CAST(b.Band AS NVARCHAR(2)) + N' descriptor text.',
                     N'Binary gate at least ' + CAST(b.Band AS NVARCHAR(2)) + N'.'
                FROM GradingCriteria c CROSS JOIN (VALUES (0),(1),(2),(3),(4),(5),(6)) AS b(Band)
               WHERE c.SkillId = @w;

            -- Bands 2 and 3 for all four; band 4 for three, so W_GR is the one short.
            INSERT INTO GradingCriterionAnchors (CriterionId, Band, ResponseText)
              SELECT c.CriterionId, b.Band, N'Anchor at band ' + CAST(b.Band AS NVARCHAR(2)) + N'.'
                FROM GradingCriteria c CROSS JOIN (VALUES (2),(3)) AS b(Band) WHERE c.SkillId = @w;
            INSERT INTO GradingCriterionAnchors (CriterionId, Band, ResponseText)
              SELECT c.CriterionId, 4, N'Anchor at band 4.'
                FROM GradingCriteria c WHERE c.SkillId = @w AND c.Code <> 'W_GR';

            INSERT INTO GradingRubricLocks (SkillId, CertId, LadderOutcome, LadderReason, LadderRunAt)
            VALUES (@w, {certId}, 'accepted', N'Separated.', SYSDATETIMEOFFSET());

            -- One bound essay on that skill, so INV-4's anchor half has a task to judge.
            DECLARE @topic INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'writing' ORDER BY TopicId);
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
            VALUES ('writing', @topic, 'B1', 'persuasive_essay', 'demo', 'active');
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, MinWords, MaxWords, ResponseSeconds)
            VALUES (@item, 1, N'Write an essay.', 100, 200, 1800);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE SkillId = @w);
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES ({examId}, {certId}, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, CefrLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp, {examId}, @sec, @w, N'Write an essay.', 'B1', 'free_response', 100, 200, 1);
            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder, UnitNumber)
            SELECT {examId}, @sec, {certId}, v.ItemVersionId, @item, 'writing', 1, 1
            FROM ItemVersions v WHERE v.ItemId = @item;

            -- Clear it, so INV-3 does not mask this. The reviewer is not the uploader (ADM-02).
            DECLARE @b INT = (SELECT TOP 1 BatchId FROM ImportBatches WHERE CertId = {certId});
            DECLARE @rev INT = (SELECT TOP 1 UserId FROM Users WHERE Email LIKE '%rev@test.local' ORDER BY UserId);
            INSERT INTO ImportLog (BatchId, RowNo, Level, Message, ItemId, Attempt) VALUES (@b, 2, 'info', N'ok', @item, 1);
            INSERT INTO ReviewSampleDecisions (BatchId, ItemId, ActorUserId, Decision) VALUES (@b, @item, @rev, 'clear');
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv4 = Invariant(audit, ExamInvariants.RubricReady);

        Assert.False(inv4.Satisfied);
        // Named as the ONE band that is missing: "missing anchors" would send the author to re-check all
        // twelve, and the ticket forbids collapsing a deficit into a sentence that sends nowhere.
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.RubricReady),
            i => i.Message.Contains("W_GR", StringComparison.Ordinal)
                 && i.Message.Contains("band(s) 4", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);
    }

    // ---------------------------------------------------------------- INV-5: timers

    [Fact]
    public async Task A_paper_whose_served_skill_durations_do_not_sum_to_its_duration_fails_inv5()
    {
        var (_, _, _, examId, _) = await SeedBaselineAsync(Unique("INV5A"));

        // Reading is 60 and the paper declares 60. Declaring 90 is the one change: the sitting allocates
        // its phases from the SKILL's duration, so the learner would be cut off at 60 while the paper
        // promised 90.
        await database.SqlAsync($"UPDATE Exams SET DurationMinutes = 90 WHERE ExamId = {examId};");

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv5 = Invariant(audit, ExamInvariants.TimersAndProjection);

        Assert.False(inv5.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.TimersAndProjection),
            i => i.Message.Contains("sum to 60", StringComparison.Ordinal)
                 && i.Message.Contains("declares 90", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);

        AssertOnlyFailed(audit, ExamInvariants.TimersAndProjection);
    }

    // ---------------------------------------------------------------- INV-5: projection

    [Fact]
    public async Task A_cut_score_projection_that_does_not_rise_with_the_level_fails_inv5()
    {
        var (certId, _, _, examId, _) = await SeedBaselineAsync(Unique("INV5B"));

        // The expected-wrong input a lazy guard passes: three cut rows with NO overlap between
        // neighbours, so trg_CefrCutScores_NoOverlap is satisfied - but B1's range sits BELOW A2's,
        // so a higher level is reached at a lower score. The trigger cannot see it because it compares
        // rows of one skill without ordering them by the level ladder; the gate can.
        await database.SqlAsync($"""
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
            VALUES ({certId}, 1, 0, N'Test standard', '2026-01-01');
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = {certId} AND Code = 'READ');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              ({certId}, 1, @skill, 'A2', 'cut', 101, 200),
              ({certId}, 1, @skill, 'B1', 'cut', 5, 100);
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv5 = Invariant(audit, ExamInvariants.TimersAndProjection);

        Assert.False(inv5.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.TimersAndProjection),
            i => i.Message.Contains("not monotonic", StringComparison.Ordinal)
                 && i.Message.Contains("B1", StringComparison.Ordinal)
                 && i.Message.Contains("A2", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);
    }

    // ---------------------------------------------------------------- INV-6: tier

    [Fact]
    public async Task A_production_paper_that_binds_demo_stock_fails_inv6_and_names_the_item()
    {
        var (_, _, _, examId, itemId) = await SeedBaselineAsync(Unique("INV6B"));

        // The triggers refuse this at the moment of the write, so the fixture reaches the state the way
        // the database allows: promote the ITEM first, promote the paper, then demote the item.
        // trg_Exams_TierKeepsContent fires on the exam's tier, so demoting the item is a real path to
        // this state - which is exactly why the rail must show it and not only the trigger.
        await database.SqlAsync($"""
            UPDATE Items SET ContentTier = 'production' WHERE ItemId = {itemId};
            UPDATE Exams SET ContentTier = 'production' WHERE ExamId = {examId};
            UPDATE Items SET ContentTier = 'demo' WHERE ItemId = {itemId};
            """);

        var audit = await CreateService().AuditExamPublishReadinessAsync(examId);
        var inv6 = Invariant(audit, ExamInvariants.TierIsolation);

        Assert.False(inv6.Satisfied);
        Assert.Contains(
            audit.Issues.Where(i => i.Code == ExamInvariants.TierIsolation),
            i => i.Message.Contains($"demo-tier item {itemId}", StringComparison.Ordinal));
        Assert.False(audit.CanPublish);
    }

    // ---------------------------------------------------------------- the door

    [Fact]
    public async Task Publishing_records_the_pass_before_the_status_moves()
    {
        var (_, _, _, examId, _) = await SeedBaselineAsync(Unique("DOOR"));

        var service = CreateService();
        var audit = await service.AuditExamPublishReadinessAsync(examId);
        Assert.True(audit.CanPublish, string.Join(" | ", audit.Issues.Select(i => i.Message)));

        var result = await service.PublishExamAsync(examId);
        Assert.True(result.Success);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var pass = await db.ExamPublishAudits.SingleAsync(a => a.ExamId == examId);
        var exam = await db.Exams.SingleAsync(e => e.ExamId == examId);

        Assert.Equal(BusinessObjects.Entities.ExamPublishAudit.Writer, pass.WrittenByService);
        Assert.Equal(ExamStatus.Published.ToCode(), exam.Status);
        Assert.True(pass.PassedAt >= exam.UpdatedAt, "the pass must be at least as new as the edit it unlocked");
    }

    /// <summary>
    /// The bypass the ticket is about, at the seam a service can see: a raw status write after a
    /// structural edit must be refused by the DATABASE, not by the service that is not being called.
    /// <para>
    /// <b>Why this test flips the catalog strict.</b> The test database is a fixture catalog, and the
    /// trigger is exempt there for the reason the five #201 guards are - so in the default mode this
    /// assertion would pass on a trigger that never ran. The flip is restored in a <c>finally</c>, and
    /// the collection is serialised, so no other fixture can observe it. <c>domain41</c> proves the same
    /// refusal in T-SQL; this proves it survives the EF path, which is the one the app actually uses.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_raw_status_write_after_a_structural_edit_is_refused_by_the_database()
    {
        var (_, _, sectionId, examId, _) = await SeedBaselineAsync(Unique("RAW"));

        var service = CreateService();
        await service.PublishExamAsync(examId);
        await service.WithdrawExamAsync(examId);

        await database.SqlAsync("UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;");
        try
        {
            // A structural edit that breaks INV-1, then an attempt to ride the first publish's pass.
            // The Section write declares an author, because the triage guard refuses an undeclared one
            // and a fixture that tripped THAT guard would report its refusal as this file's trigger.
            var staffId = await SeedStaffAccountAsync();
            await database.SqlAsync($"""
                UPDATE Sections SET ExpectedQuestionCount = 5, LastWrittenBy = {staffId} WHERE SectionId = {sectionId};
                UPDATE Exams SET UpdatedAt = SYSDATETIMEOFFSET(), LastWrittenBy = {staffId} WHERE ExamId = {examId};
                """);

            var ex = await ExecExpectingFailureAsync(
                $"UPDATE Exams SET Status = 'published', LastWrittenBy = {staffId} WHERE ExamId = {examId};");

            // The trigger's own number and sentence, not "an error happened": the pass is stale.
            Assert.Equal(50101, ex.Number);
            Assert.Contains("no recorded readiness pass newer than its last edit", ex.Message, StringComparison.Ordinal);

            // And the paper is still withdrawn, so the refusal rolled the transition back rather than
            // leaving it half-applied.
            await using var db = DbContextFactory.Create(database.ConnectionString);
            Assert.Equal(
                ExamStatus.Hidden.ToCode(),
                await db.Exams.Where(e => e.ExamId == examId).Select(e => e.Status).SingleAsync());
        }
        finally
        {
            await database.SqlAsync("UPDATE CatalogMode SET IsFixtureCatalog = 1 WHERE ModeId = 1;");
        }
    }

    /// <summary>
    /// The other half of the door, through EF: in a strict catalog the pass must reach the database
    /// BEFORE the status moves, or the trigger refuses a paper that passed every invariant. This is the
    /// command-ordering risk the two-step save exists to remove, and it is invisible in a fixture
    /// catalog because the trigger returns early there.
    /// </summary>
    [Fact]
    public async Task Publishing_succeeds_in_a_strict_catalog_so_the_pass_reaches_the_trigger_first()
    {
        var (_, _, _, examId, _) = await SeedBaselineAsync(Unique("STRICT"));
        var staffId = await SeedStaffAccountAsync();

        await database.SqlAsync("UPDATE CatalogMode SET IsFixtureCatalog = 0 WHERE ModeId = 1;");
        try
        {
            var service = CreateServiceAs(staffId);
            var result = await service.PublishExamAsync(examId);

            Assert.True(result.Success);

            await using var db = DbContextFactory.Create(database.ConnectionString);
            Assert.Equal(
                ExamStatus.Published.ToCode(),
                await db.Exams.Where(e => e.ExamId == examId).Select(e => e.Status).SingleAsync());
            Assert.Equal(1, await db.ExamPublishAudits.CountAsync(a => a.ExamId == examId));
        }
        finally
        {
            await database.SqlAsync("UPDATE CatalogMode SET IsFixtureCatalog = 1 WHERE ModeId = 1;");
        }
    }
}
