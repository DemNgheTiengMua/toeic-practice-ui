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

    private static CreateQuestionRequest ReadableQuestion(int groupId) => new(
        groupId,
        "Choose the correct word.",
        QuestionTypeDto.Mcq,
        Options:
        [
            new AdminQuestionOptionInput(null, "A", "Correct", true, null),
            new AdminQuestionOptionInput(null, "B", "Wrong", false, null),
        ],
        CefrLevelCode: "B1");

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

    /// <summary>An exam holding one section's worth of answerable questions.</summary>
    private async Task<int> SeedCompleteExamAsync(AdminExamService service, int certId, int sectionId, string name)
    {
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, name, 60, ExamPurposeDto.Mock, QuestionCount: 2));

        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sectionId));
        await service.CreateQuestionAsync(exam.ExamId, ReadableQuestion(group.GroupId));
        await service.CreateQuestionAsync(exam.ExamId, ReadableQuestion(group.GroupId));
        return exam.ExamId;
    }

    /// <summary>
    /// Records one finished attempt. The trigger only admits published exams, so the fixture flips
    /// the status in SQL — it needs a published exam, not an audited one.
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

        // a. adding a question changes the section total the attempt was scored against
        var addQuestion = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.CreateQuestionAsync(examId, ReadableQuestion(group.GroupId)));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, addQuestion.Code);
        // The count in context, not any "1" in the message: an admin has to read how many learners
        // this affects before deciding what to do.
        Assert.Contains("1 learner attempt has already finished it", addQuestion.Message, StringComparison.Ordinal);

        // b. adding a group is the same structural change one level up
        var addGroup = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.CreateQuestionGroupAsync(examId, new CreateQuestionGroupRequest(sectionId)));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, addGroup.Code);

        // c. changing the answer key would rewrite what the finished attempt got right
        var changeKey = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.UpdateQuestionAsync(question.QuestionId, new UpdateQuestionRequest(
                group.GroupId,
                question.Stem,
                question.QuestionType,
                Options:
                [
                    new AdminQuestionOptionInput(null, "A", "Correct", false, null),
                    new AdminQuestionOptionInput(null, "B", "Wrong", true, null),
                ], CefrLevelCode: "B1")));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, changeKey.Code);

        // d. deleting a question shrinks a section a finished attempt was scored on
        var deleteQuestion = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.DeleteQuestionAsync(question.QuestionId));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, deleteQuestion.Code);

        // e. deleting the group takes its questions with it
        var deleteGroup = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.DeleteQuestionGroupAsync(examId, group.GroupId));
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
        Assert.Equal(2, await db.Questions.CountAsync(q => q.ExamId == examId));
    }

    [Fact]
    public async Task Editing_the_answer_key_of_an_inline_completion_question_is_a_structural_edit()
    {
        var (certId, sectionId) = await SeedCertificateAsync();
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Key Locked {Guid.NewGuid():N}"[..30], 60, ExamPurposeDto.Mock));
        var group = await service.CreateQuestionGroupAsync(exam.ExamId, new CreateQuestionGroupRequest(sectionId));

        // An inline-completion question is graded against AcceptedAnswersJson, not against an option
        // set, so this is the answer key the lock has to cover.
        var question = await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "The report was filed _____ Friday.",
            QuestionTypeDto.InlineCompletion,
            AcceptedAnswersJson: """["before"]""", CefrLevelCode: "B1"));
        await service.CreateQuestionAsync(exam.ExamId, new CreateQuestionRequest(
            group.GroupId,
            "Second gap _____ the deadline.",
            QuestionTypeDto.InlineCompletion,
            AcceptedAnswersJson: """["before"]""", CefrLevelCode: "B1"));

        await RecordFinishedAttemptAsync(exam.ExamId, certId);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateQuestionAsync(
            question.QuestionId,
            new UpdateQuestionRequest(
                group.GroupId, question.Stem, question.QuestionType,
                AcceptedAnswersJson: """["after"]""", CefrLevelCode: "B1")));

        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex.Code);

        // The stem alone is cosmetic and still saves, which is what keeps the rule structural
        // rather than a blanket freeze.
        var renamed = await service.UpdateQuestionAsync(question.QuestionId, new UpdateQuestionRequest(
            group.GroupId, "Reworded gap.", question.QuestionType,
            AcceptedAnswersJson: """["before"]""", CefrLevelCode: "B1"));
        Assert.Equal("Reworded gap.", renamed.Stem);
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
        var group = studio.Sections.SelectMany(s => s.Groups).First();
        var question = group.Questions.First();

        // The contract keeps name, description and internal note editable; so do stem, explanation
        // and hint, which change no score.
        var exam = await service.GetExamByIdAsync(examId);
        var renamed = await service.UpdateExamAsync(examId, new UpdateAdminExamRequest(
            certId, "Renamed After Sitting", exam.DurationMinutes, exam.Purpose,
            Description: "New description",
            InternalNote: "New note"));

        Assert.Equal("Renamed After Sitting", renamed.Name);
        Assert.Equal("New note", renamed.InternalNote);

        var edited = await service.UpdateQuestionAsync(question.QuestionId, new UpdateQuestionRequest(
            group.GroupId,
            "Reworded stem.",
            question.QuestionType,
            Explanation: "Clearer explanation",
            Hint: "Look at the verb",
            Options: question.Options
                .Select(o => new AdminQuestionOptionInput(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                .ToList(), CefrLevelCode: "B1"));

        Assert.Equal("Reworded stem.", edited.Stem);
        Assert.Equal("Clearer explanation", edited.Explanation);
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
