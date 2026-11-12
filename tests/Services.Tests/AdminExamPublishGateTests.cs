using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #157: nothing may publish an exam that fails audit.
/// <para>
/// Before this, audit guarded only POST /{id}/publish. The Status dropdown in Exam Editor step 1
/// wrote Published straight onto an empty exam, so an unaudited exam could go live in one form
/// post. These tests pin each door shut, at the service seam the controller calls.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminExamPublishGateTests(DatabaseFixture database)
{
    private AdminExamService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    /// <summary>A certificate with one readable section that declares two questions.</summary>
    private async Task<(int CertId, int SectionId)> SeedCertificateAsync()
    {
        var certCode = $"GATE_{Guid.NewGuid():N}"[..16];
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Gate Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 5, 495, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);
        return (cert.CertId, section.SectionId);
    }

    /// <summary>
    /// Runs a batch that must be rejected. <see cref="DatabaseFixture.SqlAsync"/> turns a non-zero
    /// sqlcmd exit into an assertion failure, so a constraint violation would look like a broken
    /// fixture rather than the behaviour under test; this opens the connection directly.
    /// </summary>
    private async Task<int> ExecSqlAsync(string batch)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(batch, connection);
        return await command.ExecuteNonQueryAsync();
    }

    private static QuickIngestParsedQuestionDto ParsedQuestion() => new(
        Index: 1,
        StartLineNumber: 1,
        EndLineNumber: 2,
        Stem: "Added through quick ingest",
        Explanation: null,
        CategoryCode: null,
        Options:
        [
            new QuickIngestParsedOptionDto("A", "Yes", true),
            new QuickIngestParsedOptionDto("B", "No", false),
        ],
        Issues: [],
        IsValid: true);

    /// <summary>A pool item to bind, at the section's own modality and with four options (CNT-06).</summary>
    private static CreatePoolItemRequest ReadableItem(int topicId) => new(
        ModalityDto.Reading,
        topicId,
        "B1",
        "sentence_cloze",
        "Choose the correct word.",
        // Gate one refuses a pair closer than a few characters, and a key far shorter than its
        // distractors: all four are near the same length and mutually distinct.
        OptionA: "The depot opens at six in the morning.",
        OptionB: "The ledger is signed after the gate closes.",
        OptionC: "Visitors wait outside until the supervisor arrives.",
        OptionD: "Only the supervisor may open the loading bay.",
        KeyLabel: "A",
        Explanation: "The passage states the opening time.",
        RationaleA: "Correct: the passage states the opening time.",
        RationaleB: "Wrong: the passage gives no ledger time.",
        RationaleC: "Wrong: visitors wait, but that is not the opening time.",
        RationaleD: "Wrong: the supervisor opens the gate, not the bay.");

    /// <summary>
    /// The reading topic every authored item joins. The vocabulary is a closed set of twenty, so a
    /// canonical code is reused rather than invented.
    /// </summary>
    private async Task<int> ReadingTopicIdAsync()
    {
        await database.SqlAsync("""
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();
    }

    /// <summary>An exam holding one section's worth of answerable positions.</summary>
    private async Task<int> SeedCompleteExamAsync(AdminExamService service, int certId, int sectionId, string name)
    {
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, name, 60, ExamPurposeDto.Mock, QuestionCount: 2));

        await BindReadableItemAsync(service, exam.ExamId, sectionId, 1);
        await BindReadableItemAsync(service, exam.ExamId, sectionId, 2);
        return exam.ExamId;
    }

    /// <summary>
    /// One pool item bound at a position. #269: authoring is <c>IAdminItemService.CreateAsync</c> plus
    /// <c>AdminExamService.BindItemAsync</c> - the two acts the deleted create/delete pair performed.
    /// </summary>
    private async Task BindReadableItemAsync(AdminExamService service, int examId, int sectionId, int position)
    {
        var topicId = await ReadingTopicIdAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var created = await new AdminItemService(db, TimeProvider.System).CreateAsync(ReadableItem(topicId));

        await service.BindItemAsync(examId, new BindPoolItemRequest(sectionId, created.ItemVersionId, position));
    }

    /// <summary>
    /// Records one finished attempt. The trigger only admits published exams, so the fixture flips
    /// the status in SQL â€” it needs a published exam, not an audited one.
    /// </summary>
    private async Task RecordFinishedAttemptAsync(int examId, int certId)
    {
        var email = $"{Guid.NewGuid():N}@gate.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Gate Learner');
            DECLARE @userId INT = SCOPE_IDENTITY();

            UPDATE Exams SET Status = 'published' WHERE ExamId = {examId};

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, SubmittedAt, ExpiresAt)
            VALUES (@userId, {examId}, {certId}, 'graded', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
                    DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            """);
    }

    // ---------------------------------------------------------------- rule 1+2: Status is ignored

    [Fact]
    public async Task A_create_request_that_still_sends_status_published_still_leaves_a_draft()
    {
        var (certId, _) = await SeedCertificateAsync();
        var service = CreateService();

        // AC2, at the wire: an old client (or curl) that still posts "status":"published" must have
        // it ignored, not honoured. Deserialising the JSON the API would receive proves the field is
        // not bound; the exam then comes back a draft.
        var payload = $$"""
            {
              "certId": {{certId}},
              "name": "Ignore Status",
              "durationMinutes": 60,
              "purpose": "mock",
              "status": "published"
            }
            """;
        var request = System.Text.Json.JsonSerializer.Deserialize<CreateAdminExamRequest>(
            payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var created = await service.CreateExamAsync(request);

        Assert.Equal(ExamStatusDto.Draft, created.Status);
        var reloaded = await service.GetExamByIdAsync(created.ExamId);
        Assert.Equal(ExamStatusDto.Draft, reloaded.Status);
    }

    [Fact]
    public async Task An_update_request_that_still_sends_status_published_does_not_publish()
    {
        var (certId, _) = await SeedCertificateAsync();
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Keep Draft {Guid.NewGuid():N}"[..30], 60, ExamPurposeDto.Mock));

        var payload = $$"""
            {
              "certId": {{certId}},
              "name": "Renamed While Draft",
              "durationMinutes": 75,
              "purpose": "mock",
              "status": "published",
              "description": "edited"
            }
            """;
        var request = System.Text.Json.JsonSerializer.Deserialize<UpdateAdminExamRequest>(
            payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var renamed = await service.UpdateExamAsync(exam.ExamId, request);

        // The metadata in the same request still saves; the status does not move.
        Assert.Equal("Renamed While Draft", renamed.Name);
        Assert.Equal(75, renamed.DurationMinutes);
        Assert.Equal("edited", renamed.Description);
        Assert.Equal(ExamStatusDto.Draft, renamed.Status);
    }

    // ------------------------------------------------------- rule 3: TargetQuestionCount NOT NULL

    [Fact]
    public async Task A_section_cannot_be_created_without_a_declared_question_count()
    {
        var (certId, _) = await SeedCertificateAsync();

        // Without the count, the per-section audit check (SEC_QUESTION_COUNT_LOW) had nothing to
        // compare against, so an empty part passed publish unnoticed.
        var ex = await Assert.ThrowsAsync<SqlException>(() => ExecSqlAsync($"""
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId});
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, DisplayOrder)
            VALUES (@skill, {certId}, 'reading', 'NOCOUNT', N'No declared count', 4, 2);
            """));

        Assert.Contains("ExpectedQuestionCount", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_section_cannot_declare_zero_questions()
    {
        var (certId, _) = await SeedCertificateAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => ExecSqlAsync($"""
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId});
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, {certId}, 'reading', 'ZERO', N'Zero declared', 0, 3);
            """));

        Assert.Contains("CK_Sections_ExpectedCount", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- rule 4: structure locked on finished attempts

    /// <summary>
    /// #157: once an exam has a finished attempt, its structure is what that attempt was scored
    /// against, so the three structural acts on a pool paper are refused by the same lock.
    /// <para>
    /// <b>What changed.</b> This drove the deleted create/update/delete question and group pair. The
    /// durable rule is that adding a position and adding a group are structural edits; on the pool those
    /// are <c>BindItemAsync</c> and <c>CreateExamClusterAsync</c>, and both call
    /// <c>RequireStructureEditableAsync</c>. The assertions - the error code and the count in the
    /// message - are unchanged. A key change is the third act and has no reachable caller; see (c).
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_exam_with_a_finished_attempt_refuses_structural_edits_and_names_the_count()
    {
        var (certId, sectionId) = await SeedCertificateAsync();
        var service = CreateService();

        var examId = await SeedCompleteExamAsync(service, certId, sectionId, $"Locked {Guid.NewGuid():N}"[..30]);
        await RecordFinishedAttemptAsync(examId, certId);

        var studio = await service.GetExamStudioAsync(examId);
        var group = studio.Sections.SelectMany(s => s.Groups).First();
        var question = group.Questions.First();

        // a. binding one more position changes the section total the attempt was scored against
        var topicId = await ReadingTopicIdAsync();
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var created = await new AdminItemService(db, TimeProvider.System).CreateAsync(ReadableItem(topicId));
            var addPosition = await Assert.ThrowsAsync<AdminExamException>(() =>
                service.BindItemAsync(examId, new BindPoolItemRequest(sectionId, created.ItemVersionId, 3)));
            Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, addPosition.Code);
            // The count in context, not any "1" in the message: an admin has to read how many learners
            // this affects before deciding what to do.
            Assert.Contains("1 learner attempt has already finished it", addPosition.Message, StringComparison.Ordinal);
        }

        // b. adding a group is the same structural change one level up
        var addGroup = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.CreateExamClusterAsync(examId, new CreateExamClusterRequest(sectionId, QuestionCount: 1, CefrLevelCode: "B1")));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, addGroup.Code);

        // c. changing the answer key would rewrite what the finished attempt got right.
        //
        // NOT EXPRESSIBLE HERE - reported rather than faked. The pool's key-change fork is
        // AdminExamService.UpdatePoolPositionAsync (AdminExamService.Questions.cs:48-97, lock at :96),
        // which is PRIVATE and called by nothing. The Studio autosave route that would reach it posts to
        // PUT /api/admin/questions/{id}, which #265 removed, and IAdminItemService.UpdateAsync names no
        // paper so it has no lock to consult. The rule is still enforced in that method; only a caller
        // is missing, and inventing one here would prove nothing.

        // d. removing the group shrinks a section a finished attempt was scored on
        var deleteGroup = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.DeleteExamStudioGroupAsync(examId, question.GroupId));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, deleteGroup.Code);
    }

    [Fact]
    public async Task Quick_ingest_cannot_add_questions_to_an_exam_with_a_finished_attempt()
    {
        var (certId, sectionId) = await SeedCertificateAsync();
        var service = CreateService();

        var examId = await SeedCompleteExamAsync(service, certId, sectionId, $"QI Locked {Guid.NewGuid():N}"[..30]);
        await RecordFinishedAttemptAsync(examId, certId);
        // A second attempt, so the message's plural branch is the one on the wire here and the
        // singular form is covered by the admin-service test above.
        await RecordFinishedAttemptAsync(examId, certId);

        // Through the quick-ingest service itself, not AdminExamService: commits used to insert
        // groups and questions straight into the DbContext, so the lock in the admin service never
        // saw them. This is the entry point the admin endpoint resolves.
        var quickIngest = new ExamQuickIngestService(
            DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            quickIngest.CommitStagedAsync(examId, new QuickIngestCommitRequest(
                SectionId: sectionId,
                Mode: "append",
                Questions: [ParsedQuestion()], CefrLevel: "B1")));

        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex.Code);
        Assert.Contains("2 learner attempts have already finished it", ex.Message, StringComparison.Ordinal);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        // #269: Questions is gone, so "the refused commit wrote no row" is measured on the roster the
        // pool commit would have extended.
        Assert.Equal(2, await db.ExamItems.CountAsync(i => i.ExamId == examId));
    }

    [Fact]
    public async Task A_sat_exam_can_be_withdrawn_and_leaves_the_learner_catalog()
    {
        var (certId, sectionId) = await SeedCertificateAsync();
        var service = CreateService();

        var examId = await SeedCompleteExamAsync(service, certId, sectionId, $"Withdrawn {Guid.NewGuid():N}"[..30]);
        await RecordFinishedAttemptAsync(examId, certId);

        // #157 removed the only writer of 'hidden', which left a retired exam with no state to
        // reach: it cannot be deleted once it has been sat. Withdraw is that deliberate route,
        // and it is not a publish door - it only ever removes the exam from the catalog.
        await service.WithdrawExamAsync(examId);

        var detail = await service.GetExamByIdAsync(examId);
        Assert.Equal(ExamStatusDto.Hidden, detail.Status);

        // Every learner-facing catalog filters on 'published', and the attempt trigger admits only
        // published exams, so hidden really is out of reach for a learner.
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reachable = await db.Exams.CountAsync(e =>
            e.ExamId == examId && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted);
        Assert.Equal(0, reachable);
    }

    [Fact]
    public async Task An_exam_with_a_finished_attempt_still_accepts_cosmetic_edits()
    {
        var (certId, sectionId) = await SeedCertificateAsync();
        var service = CreateService();

        var examId = await SeedCompleteExamAsync(service, certId, sectionId, $"Cosmetic {Guid.NewGuid():N}"[..30]);
        await RecordFinishedAttemptAsync(examId, certId);

        var studio = await service.GetExamStudioAsync(examId);
        var question = studio.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Questions).First();

        // The contract keeps name, description and internal note editable; so do stem, explanation
        // and hint, which change no score.
        var exam = await service.GetExamByIdAsync(examId);
        var renamed = await service.UpdateExamAsync(examId, new UpdateAdminExamRequest(
            certId, "Renamed After Sitting", exam.DurationMinutes, exam.Purpose,
            Description: "New description",
            InternalNote: "New note"));

        Assert.Equal("Renamed After Sitting", renamed.Name);
        Assert.Equal("New note", renamed.InternalNote);

        // A pool item's stem and explanation are editable through the pool door, which carries no lock:
        // the edit forks a version and leaves the one a finished attempt pinned byte-identical, so the
        // rule "cosmetic content stays editable after a sitting" holds for the same reason.
        var topicId = await ReadingTopicIdAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var itemService = new AdminItemService(db, TimeProvider.System);
        var version = await db.ItemVersions.AsNoTracking()
            .Where(v => db.ExamItems.Any(i => i.ExamItemId == question.QuestionId && i.ItemVersionId == v.ItemVersionId))
            .SingleAsync();

        var edited = await itemService.UpdateAsync(version.ItemId, new UpdatePoolItemRequest(
            Stem: "Reworded stem.",
            OptionA: version.OptionA,
            OptionB: version.OptionB,
            OptionC: version.OptionC,
            OptionD: version.OptionD,
            KeyLabel: version.KeyLabel,
            Explanation: "Clearer explanation",
            RationaleA: version.RationaleA,
            RationaleB: version.RationaleB,
            RationaleC: version.RationaleC,
            RationaleD: version.RationaleD));

        Assert.Equal("Reworded stem.", edited.Stem);
        Assert.Equal("Clearer explanation", edited.Explanation);
        Assert.Equal(2, edited.VersionNumber);
    }

    // ------------------------------------- rule 5: a refused publish names the failed checks

    [Fact]
    public async Task Publishing_an_empty_exam_names_the_failed_audit_codes()
    {
        var (certId, _) = await SeedCertificateAsync();
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Empty {Guid.NewGuid():N}"[..30], 60, ExamPurposeDto.Mock));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(exam.ExamId));

        Assert.Equal(ApiError.Codes.ExamIncomplete, ex.Code);
        Assert.Equal(400, ex.Status);
        // The admin has to fix it from the text, so the message carries the audit codes, not just
        // "incomplete".
        Assert.Contains("EXAM_EMPTY", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SEC_QUESTION_COUNT_LOW", ex.Message, StringComparison.Ordinal);
    }
}
