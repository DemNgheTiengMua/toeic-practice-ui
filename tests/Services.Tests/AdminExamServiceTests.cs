using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// <c>AdminExamService</c>'s durable rules: the publish door and its audit codes, the exam lifecycle
/// guards, cluster authorship, quick ingest, export and the Studio reads.
/// <para>
/// <b>#269: what left this file.</b> Its old subject was <c>CreateQuestionGroupAsync</c> /
/// <c>CreateQuestionAsync</c> / <c>UpdateQuestionAsync</c> / <c>DeleteQuestionAsync</c>, which went with
/// the <c>QuestionGroups</c> / <c>Questions</c> / <c>QuestionOptions</c> tables. A test whose subject is
/// a deleted member has nothing left to assert and is gone; a test whose subject was a durable RULE is
/// kept and re-expressed on the pool - <c>PoolFixture</c> for a fixture, and the pool writers for a
/// write - with its assertion intact. Every removal is named where it stood.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminExamServiceTests(DatabaseFixture database)
{
    private AdminExamService CreateService()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AdminExamService(db, TimeProvider.System);
    }

    /// <summary>
    /// #157: an exam can no longer be created already-published. These fixtures need a published
    /// exam only because trg_ExamAttempts_PublishedExamOnly requires one before an attempt may be
    /// recorded, so they promote it in SQL instead of walking the whole publish audit.
    /// </summary>
    private Task PublishForFixtureAsync(int examId) =>
        database.SqlAsync($"UPDATE Exams SET Status = 'published' WHERE ExamId = {examId};");

    /// <summary>
    /// #226 / INV-3: a bound item reaches a learner only after a human clearance names it, and ADM-02's
    /// trigger refuses a decision made by the batch's uploader or the item's last writer. The reviewer
    /// is therefore a second account, which is the invariant rather than a fixture inconvenience.
    /// </summary>
    private async Task ClearItemsForPublishAsync(int certId, IReadOnlyList<int> itemIds)
    {
        var uploaderEmail = $"{Guid.NewGuid():N}@uploader.local";
        var reviewerEmail = $"{Guid.NewGuid():N}@reviewer.local";

        var logs = string.Join(", ", itemIds.Select((id, index) => $"(@batch, {index + 1}, 'info', N'ok', {id}, 1)"));
        var decisions = string.Join(", ", itemIds.Select(id => $"(@batch, {id}, @reviewer, 'clear', SYSDATETIMEOFFSET())"));

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'reviewer')
              INSERT INTO Roles (Code, Name) VALUES ('reviewer', N'Reviewer');
            DECLARE @role INT = (SELECT RoleId FROM Roles WHERE Code = 'reviewer');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES (@role, '{uploaderEmail}', 0x01, N'Uploader');
            DECLARE @uploader INT = SCOPE_IDENTITY();

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES (@role, '{reviewerEmail}', 0x01, N'Reviewer');
            DECLARE @reviewer INT = SCOPE_IDENTITY();

            INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
            VALUES ({certId}, N'fixture.xlsx', 'done', @uploader);
            DECLARE @batch INT = SCOPE_IDENTITY();

            INSERT INTO ImportLog (BatchId, RowNo, Level, Message, ItemId, Attempt) VALUES {logs};
            """);
    }

    /// <summary>A topic of this modality, authored when the fresh catalog holds none.</summary>
    private static async Task<int> TopicIdAsync(CompanionDbContext db, string modality)
    {
        var existing = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == modality)
            .OrderBy(t => t.TopicId)
            .Select(t => (int?)t.TopicId)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return existing.Value;
        }

        var (code, name) = modality switch
        {
            "listening" => ("L-DET", "Listening for detail"),
            "speaking" => ("S-DESC", "Describing"),
            "writing" => ("W-CORR", "Transactional correspondence"),
            _ => ("R-DET", "Local factual retrieval"),
        };

        var topic = new Topic { Modality = modality, Code = code, Name = name };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.TopicId;
    }

    /// <summary>
    /// One MCQ position carrying an evidence span, which is what INV-2 judges a bundle by. Written here
    /// rather than through <see cref="PoolFixture"/> because a span is a scored field of the version and
    /// the fixture authors none - a version with a null span is refused by
    /// <c>PoolGateFour.ReviewBundleAsync</c>, so a publish fixture needs it.
    /// </summary>
    private static async Task<(int ItemId, int ExamItemId)> AddSpannedMcqAsync(
        CompanionDbContext db, Exam exam, Section section, Stimulus? stimulus,
        string stem, int displayOrder, int evidenceStart, int evidenceEnd, string evidenceUnit = "char")
    {
        var topicId = await TopicIdAsync(db, section.Modality);

        var item = new Item
        {
            Modality = section.Modality,
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            // CK_Items_Modality_Type: the semantic type is the modality's own.
            ItemType = section.Modality == "listening" ? "short_exchange" : "sentence_cloze",
            RenderFormat = "mcq",
            ContentTier = "demo",
            LifecycleStatus = "active",
            StimulusId = stimulus?.StimulusId,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem,
            OptionA = "at",
            OptionB = "on",
            KeyLabel = "A",
            Explanation = "A clock time takes 'at'.",
            EvidenceStart = evidenceStart,
            EvidenceEnd = evidenceEnd,
            EvidenceUnit = evidenceUnit,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync();

        var binding = new ExamItem
        {
            ExamId = exam.ExamId,
            CertId = exam.CertId,
            SectionId = section.SectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = section.Modality,
            DisplayOrder = displayOrder,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync();

        return (item.ItemId, binding.ExamItemId);
    }

    private async Task<(int certId, int secListeningId, int secReadingId, int secSpeakingId, int secWritingId)> SeedCertificateWithSectionsAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            -- #226: a duration is required. INV-5 sums the served skills' durations and compares against
            -- the paper's DurationMinutes, because AttemptEngine.BuildAttemptPhases allocates the
            -- sitting's phases from exactly these values. A skill with none cannot sum to anything.
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'L_' + @certStr, N'Listening', 'listening', 1, 15),
              (@cert, 'R_' + @certStr, N'Reading', 'reading', 2, 15),
              (@cert, 'S_' + @certStr, N'Speaking', 'speaking', 3, 15),
              (@cert, 'W_' + @certStr, N'Writing', 'writing', 4, 15);

            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'L_' + @certStr);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'R_' + @certStr);
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'S_' + @certStr);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'W_' + @certStr);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SL_' + @certStr, N'Listening Part 1', 1, 1),
              (@sR, @cert, 'reading', 'SR_' + @certStr, N'Reading Part 5', 1, 2),
              (@sS, @cert, 'speaking', 'SS_' + @certStr, N'Speaking Part 1', 1, 3),
              (@sW, @cert, 'writing', 'SW_' + @certStr, N'Writing Part 1', 1, 4);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GRAM', N'Grammatical accuracy in context');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-DET', N'Local factual retrieval');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'listening' AND Code = 'L-DET')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('listening', 'L-DET', N'Listening for detail');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'writing' AND Code = 'W-ARG')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('writing', 'W-ARG', N'Persuasive argumentation');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'speaking' AND Code = 'S-DESC')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('speaking', 'S-DESC', N'Descriptive monologue');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var secL = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "listening");
        var secR = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "reading");
        var secS = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "speaking");
        var secW = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Modality == "writing");

        return (cert.CertId, secL.SectionId, secR.SectionId, secS.SectionId, secW.SectionId);
    }

    // ---------------------------------------------------------------- deleted: the question doors
    //
    // Gone with their subjects, by design (#269):
    //
    //   McqQuestionValidation_ValidatesOptionsAndTimingAndWords - subject CreateQuestionAsync. Its rules
    //     (two-to-four options, one key, no timers or word bounds on an MCQ) were CreateQuestionAsync's
    //     input validation. The pool's answer-shape rule is CK_ItemVersions_OptionsComplete plus
    //     AdminItemService.ValidateVersionShape, and its refusals are pinned by AdminItemAuthoringTests;
    //     the timer and word-bound rules have no writer left at all.
    //   FreeResponseValidation_ValidatesTimingWordsAndNoOptions - same subject, same reason.
    //   DeleteQuestion_BlockedWhenAttemptAnswersExist_SucceedsWhenUnattempted - subject
    //     DeleteQuestionAsync. The durable half ("an answered position cannot leave the paper") is
    //     DeleteExamStudioGroupAsync's, pinned by StudioGroupWriterTests; the other half ("a locked
    //     paper refuses it") is ExamStructureLock's, pinned by AdminExamPublishGateTests.
    //   DeleteQuestion_UnattemptedExam_StillDeletes - subject DeleteQuestionAsync. There is no admin
    //     delete-item route any more (CNT-43 retires an item rather than deleting it), so there is no
    //     door to assert.
    //   UpdateQuestion_SwitchingCorrectOption_SucceedsWithoutUniqueConstraintViolation - subject
    //     UpdateQuestionAsync. The key is a column on ItemVersions, not a row with a filtered unique
    //     index, and re-keying A to B is asserted through the only writer by
    //     AdminItemAuthoringTests.Editing_a_scored_field_creates_a_new_version_and_leaves_the_first_identical.
    //   UpdateQuestion_WhenDisplayOrderIsZeroOrNotProvided_PreservesExistingDisplayOrderAndUpdatesExplanation
    //     - subject UpdateQuestionAsync. "0 means keep the order" was that method's rule and nothing
    //     writes a position's order that way now: a reorder is ReorderSectionItemsAsync, and an
    //     explanation edit in place is AdminItemService.UpdateAsync's, pinned by AdminItemAuthoringTests.
    //   AuthoringQuestionComponentTypes_PersistsAllFieldsAndOptions - subject CreateQuestionAsync, four
    //     calls deep. The durable claim ("every render format persists its answer shape") is the pool's
    //     and is pinned by SchemaAndSixAxisModelTests, RenderFormatRepresentableTests and the pool
    //     builder in ExamRebuildFullSystemE2ETests.

    /// <summary>
    /// The group card's own validation: an image needs alt text, a slice needs both ends and an end
    /// after its start - and a complete slice is accepted.
    /// <para>
    /// #269: this drove <c>CreateQuestionGroupAsync</c>, which went with the <c>QuestionGroups</c>
    /// table. The rules did not move: <c>ValidateQuestionGroup</c> is still their only statement, and
    /// every Studio group write runs it, so the assertions are kept verbatim against
    /// <c>UpdateExamStudioGroupAsync</c>.
    /// </para>
    /// <para>
    /// The old fourth case, "a speaking-practice exam rejects a reading section", is no longer this
    /// door's: #269 moved that rule to <c>trg_ExamItems_SkillPracticeOneModality</c>, because it is a
    /// fact about the paper's POSITIONS rather than about a group card. It is asserted at the door that
    /// now expresses it - <see cref="AdminExamService.BindItemAsync"/>, the write that creates a
    /// position - in <c>BindItem_SpeakingPracticeExam_RejectsReadingSection</c> below.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UpdateExamStudioGroup_ValidatesImageAltAndAudioSlice()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"GRP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var mockExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Mock Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // The group the write addresses: a cluster with a slice, so it is a stimulus rather than a
        // solo position - a solo position has no passage or audio of its own to write.
        var cluster = await service.CreateExamClusterAsync(mockExam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            AudioPath: "/audio/group.mp3",
            AudioStartMs: 1000,
            AudioEndMs: 8000,
            QuestionCount: 1,
            CefrLevelCode: "B1"));

        // 1. ImagePath without ImageAlt throws error
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamStudioGroupAsync(
            mockExam.ExamId, cluster.GroupId, new UpdateQuestionGroupRequest(
                secL,
                ImagePath: "img/test.jpg",
                ImageAlt: null)));
        Assert.Equal(ApiError.Codes.Validation, ex1.Code);

        // 2. AudioStartMs without AudioEndMs throws error
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamStudioGroupAsync(
            mockExam.ExamId, cluster.GroupId, new UpdateQuestionGroupRequest(
                secL,
                Passage: "A short notice.",
                AudioStartMs: 1000,
                AudioEndMs: null)));
        Assert.Equal(ApiError.Codes.Validation, ex2.Code);

        // 3. AudioEndMs <= AudioStartMs throws error
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamStudioGroupAsync(
            mockExam.ExamId, cluster.GroupId, new UpdateQuestionGroupRequest(
                secL,
                Passage: "A short notice.",
                AudioStartMs: 5000,
                AudioEndMs: 4000)));
        Assert.Equal(ApiError.Codes.Validation, ex3.Code);

        // 4. Valid group succeeds
        await service.UpdateExamStudioGroupAsync(mockExam.ExamId, cluster.GroupId, new UpdateQuestionGroupRequest(
            secL,
            Passage: "A short notice.",
            AudioPath: "/audio/group.mp3",
            ImagePath: "img/q1.jpg",
            ImageAlt: "A desk with a laptop",
            AudioStartMs: 1000,
            AudioEndMs: 8000));

        var studio = await service.GetExamStudioAsync(mockExam.ExamId);
        var group = Assert.Single(studio.Sections.Single(s => s.SectionId == secL).Groups);
        Assert.Equal("A desk with a laptop", group.ImageAlt);
        // ExamAudio only resolves a window for a group that has a track to slice, so the payload names
        // one - which is what the audio slicer on that screen sends.
        Assert.Equal(1000, group.AudioStartMs);
        Assert.Equal(8000, group.AudioEndMs);
    }

    /// <summary>
    /// UI-36 / UI-38: a speaking-practice paper holds speaking sections and nothing else.
    /// <para>
    /// #269: this was a case inside the group-card test, because <c>CreateQuestionGroupAsync</c> ran
    /// <c>ValidateQuestionGroup</c> and that function states the rule. The group write cannot express it
    /// any more - it resolves a group's own section and refuses a section that is not that one. The
    /// rule itself did not weaken: <c>trg_ExamItems_SkillPracticeOneModality</c> now refuses the
    /// position, so this drives the write that creates one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BindItem_SpeakingPracticeExam_RejectsReadingSection()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"SPKMOD_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var speakingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Speaking Test {Guid.NewGuid():N}"[..25],
            30,
            ExamPurposeDto.SpeakingPractice));

        // An authored pool item with no position yet: the binding is the write under test, so the
        // fixture must not make one. Written directly because PoolFixture always binds.
        int versionId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var topicId = await TopicIdAsync(db, "reading");
            var item = new Item
            {
                Modality = "reading",
                TopicId = topicId,
                CefrLevel = CefrLevel.B1,
                ItemType = "sentence_cloze",
                RenderFormat = "mcq",
                ContentTier = "demo",
                LifecycleStatus = "active",
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var version = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = 1,
                Stem = "A reading item.",
                OptionA = "at",
                OptionB = "on",
                KeyLabel = "A",
                Explanation = "A clock time takes 'at'.",
            };
            db.ItemVersions.Add(version);
            await db.SaveChangesAsync();
            versionId = version.ItemVersionId;
        }

        // Binding it is the write the trigger judges: the paper is speaking-practice and the section is
        // reading, so the position would be a sitting of the wrong skill.
        var ex = await Assert.ThrowsAsync<AdminExamException>(() =>
            service.BindItemAsync(speakingExam.ExamId, new BindPoolItemRequest(secR, versionId, 1)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        // Named rather than "a refusal happened": a certificate mismatch and a taken position both
        // answer Validation through the same catch, so the sentence is what makes this about modality.
        Assert.Contains("can only hold sections of that skill", ex.Message, StringComparison.Ordinal);

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(0, await verify.ExamItems.CountAsync(i => i.ExamId == speakingExam.ExamId && i.SectionId == secR));
    }

    /// <summary>
    /// #157 / #226: the publish door refuses an incomplete paper with the audit's own codes, and a
    /// paper that answers every one of them publishes.
    /// <para>
    /// #269: the fixture is a pool-authored paper - one listening skill, one section declaring two
    /// items, and the positions bound on the roster. The four assertions are the ones this test always
    /// made: empty refuses, a count shortfall refuses, a listening section with no audio refuses by
    /// name, and the completed paper publishes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PublishExam_IncompleteExam_FailsWithDetails_CompleteExam_Succeeds()
    {
        var certCode = $"PUB_{Guid.NewGuid():N}"[..18];
        int examId, certId, sectionId;

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill
            {
                CertId = cert.CertId, Code = "LIST", Name = "Listening", Modality = "listening",
                DisplayOrder = 1, DurationMinutes = 30,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                CertId = cert.CertId, SkillId = skill.SkillId, Modality = "listening",
                Code = "SL1", Name = "Listening Part 1", DisplayOrder = 1,
                ExpectedQuestionCount = 2, OptionCount = 2,
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId, Name = $"Publish Test {Guid.NewGuid():N}"[..25], Status = "draft",
                DurationMinutes = 30, Purpose = "mock", QuestionCount = 2,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            examId = exam.ExamId;
            certId = cert.CertId;
            sectionId = section.SectionId;
        }

        var service = CreateService();

        // 1. Exam with 0 questions fails publishing, and says which checks failed.
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(examId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex1.Code);
        Assert.Contains("EXAM_QUESTION_COUNT_LOW", ex1.Message, StringComparison.Ordinal);

        // One position of the two the section declares. Both positions share one stimulus - a listening
        // bundle - so the paper has one group, and an uploaded master track therefore has only that
        // group to slice. Each child carries a span in seconds, which CNT-10 orders and spaces.
        int firstItem, secondItem;
        int stimulusId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var exam = await db.Exams.SingleAsync(e => e.ExamId == examId);
            var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId);

            var stimulus = new Stimulus { CefrLevel = CefrLevel.B1, DisplayOrder = 1 };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync();
            stimulusId = stimulus.StimulusId;

            (firstItem, _) = await AddSpannedMcqAsync(
                db, exam, section, stimulus, "Question 1 Stem", 1, 0, 10, "sec");
        }
        await ClearItemsForPublishAsync(certId, [firstItem]);

        // 2. QuestionCount requirement is 2, but only 1 question present -> fails
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(examId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex2.Code);
        Assert.Contains("SEC_QUESTION_COUNT_LOW", ex2.Message, StringComparison.Ordinal);

        // The second position, so the count is satisfied and only the audio is missing.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var exam = await db.Exams.SingleAsync(e => e.ExamId == examId);
            var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId);
            var stimulus = await db.Stimuli.SingleAsync(s => s.StimulusId == stimulusId);

            // 20 s after the first child's span ends: CNT-10 asks for an 8 s gap at minimum.
            (secondItem, _) = await AddSpannedMcqAsync(
                db, exam, section, stimulus, "Question 2 Listening Stem", 2, 20, 30, "sec");
        }
        await ClearItemsForPublishAsync(certId, [secondItem]);

        // 3. Listening section present, but audio is missing -> fails
        var ex3 = await Assert.ThrowsAsync<AdminExamException>(() => service.PublishExamAsync(examId));
        Assert.Equal(ApiError.Codes.ExamIncomplete, ex3.Code);
        Assert.Contains("audio", ex3.Message, StringComparison.OrdinalIgnoreCase);

        // Upload listening audio
        await service.SaveSectionAudioAsync(examId, new SaveExamSectionAudioRequest(
            sectionId,
            "audio/test_part1.mp3",
            180,
            2048576));

        // 4. Now exam is complete -> publish succeeds!
        var publishResult = await service.PublishExamAsync(examId);
        Assert.True(publishResult.Success);

        var updatedExam = await service.GetExamByIdAsync(examId);
        Assert.Equal(ExamStatusDto.Published, updatedExam.Status);
    }

    [Fact]
    public async Task DeleteExam_BlockedWhenFinishedAttemptsExist_CanSoftDeleteOtherwise()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"EXAM_DEL_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Finished Attempt Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));
        await PublishForFixtureAsync(exam.ExamId);

        var unattemptedExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Unattempted Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // Create a finished attempt on exam
        var userEmail = $"{Guid.NewGuid():N}@finished.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Finished Learner');
            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES (@userId, {exam.ExamId}, {certId}, 'graded', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            """);

        // 1. Deleting exam with finished attempts is blocked
        var ex1 = await Assert.ThrowsAsync<AdminExamException>(() => service.DeleteExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex1.Code);

        // 2. Changing purpose or certificate of exam with finished attempts is blocked
        var ex2 = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamAsync(exam.ExamId, new UpdateAdminExamRequest(
            certId,
            exam.Name,
            exam.DurationMinutes,
            ExamPurposeDto.Placement)));
        Assert.Equal(ApiError.Codes.ExamHasFinishedAttempts, ex2.Code);

        // 3. Deleting unattempted exam succeeds
        await service.DeleteExamAsync(unattemptedExam.ExamId);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbExam = await db.Exams.SingleAsync(e => e.ExamId == unattemptedExam.ExamId);
        Assert.True(dbExam.IsDeleted);
    }

    [Fact]
    public async Task CreateExam_RejectsDuplicateNameInSameCertificate()
    {
        var (certId, _, _, _, _) = await SeedCertificateWithSectionsAsync($"DUP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var examName = $"Unique Exam Name {Guid.NewGuid():N}"[..25];
        await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            examName,
            90,
            ExamPurposeDto.Mock));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            examName,
            90,
            ExamPurposeDto.Mock)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateExam_BlockedWhenChangingCertificateWithExistingContent()
    {
        var (cert1Id, _, sec1R, _, _) = await SeedCertificateWithSectionsAsync($"C1_{Guid.NewGuid():N}"[..18]);
        var (cert2Id, _, _, _, _) = await SeedCertificateWithSectionsAsync($"C2_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert1Id,
            $"Cert Change Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // #269: content is the roster. A legacy group is no longer content this rule can see, so the
        // fixture puts a bound position on the paper instead - which is what an exam with content
        // actually looks like now.
        await database.SqlAsync($"""
            INSERT INTO Topics (Modality, Code, Name)
            SELECT 'reading', 'R-GRAM', N'Grammatical accuracy'
            WHERE NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM');

            DECLARE @topic INT = (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM');
            DECLARE @author INT = (SELECT MIN(UserId) FROM Users);

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, RenderFormat, ContentTier, LifecycleStatus, LastWrittenBy)
            VALUES ('reading', @topic, 'B1', 'sentence_cloze', 'mcq', 'demo', 'active', @author);
            DECLARE @item INT = SCOPE_IDENTITY();

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, LastWrittenBy)
            VALUES (@item, 1, N'Cert-change probe', N'a', N'b', N'c', N'd', 'A', @author);
            DECLARE @version INT = SCOPE_IDENTITY();

            INSERT INTO ExamItems (ExamId, CertId, SectionId, ItemId, ItemVersionId, Modality, DisplayOrder, LastWrittenBy)
            VALUES ({exam.ExamId}, {cert1Id}, {sec1R}, @item, @version, 'reading', 1, @author);
            """);

        // Changing certId when exam has question groups is blocked
        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.UpdateExamAsync(exam.ExamId, new UpdateAdminExamRequest(
            cert2Id,
            exam.Name,
            exam.DurationMinutes,
            ExamPurposeDto.Mock)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("Cannot change certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteExam_BlockedWhenInProgressAttemptsExist()
    {
        var (certId, _, _, _, _) = await SeedCertificateWithSectionsAsync($"INP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"In-Progress Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));
        await PublishForFixtureAsync(exam.ExamId);

        var userEmail = $"{Guid.NewGuid():N}@inprogress.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{userEmail}', 0x01, N'Active Learner');
            DECLARE @userId INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt)
            VALUES (@userId, {exam.ExamId}, {certId}, 'in_progress', SYSDATETIMEOFFSET(), DATEADD(HOUR, 2, SYSDATETIMEOFFSET()));
            """);

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.DeleteExamAsync(exam.ExamId));
        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("in progress", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSectionAudio_RejectsNonListeningSection()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"AUD_MOD_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Audio Modality Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var ex = await Assert.ThrowsAsync<AdminExamException>(() => service.SaveSectionAudioAsync(exam.ExamId, new SaveExamSectionAudioRequest(
            secR,
            "audio/reading.mp3",
            120,
            1048576)));

        Assert.Equal(ApiError.Codes.Validation, ex.Code);
        Assert.Contains("listening", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitQuickIngest_AppendMode_AppendsQuestionsWithIncrementedOrder()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"QI_APP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Quick Ingest Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // #266: the importer writes only the pool, and an imported item is demo stock (INV-6). A
        // production paper therefore receives the items but not the bindings - correct, and not what
        // this test is about. The paper is demo so the batch can bind, which is the case the append and
        // replace behaviour is defined for.
        await database.SqlAsync($"UPDATE Exams SET ContentTier = 'demo' WHERE ExamId = {exam.ExamId}");

        var q1 = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Initial question stem",
            Explanation: "Initial explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Option A", true),
                new QuickIngestParsedOptionDto("B", "Option B", false),
                new QuickIngestParsedOptionDto("C", "Option C", false),
                new QuickIngestParsedOptionDto("D", "Option D", false)
            ],
            Issues: [],
            IsValid: true);

        var commit1 = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [q1], CefrLevel: "B1"));

        Assert.True(commit1.Success);
        Assert.Equal(1, commit1.QuestionsCommitted);

        // Now append a second batch
        var q2 = new QuickIngestParsedQuestionDto(
            Index: 2,
            StartLineNumber: 6,
            EndLineNumber: 10,
            Stem: "Second question stem",
            Explanation: "Second explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Option 2A", false),
                new QuickIngestParsedOptionDto("B", "Option 2B", true),
                new QuickIngestParsedOptionDto("C", "Option 2C", false),
                new QuickIngestParsedOptionDto("D", "Option 2D", false)
            ],
            Issues: [],
            IsValid: true);

        var commit2 = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [q2], CefrLevel: "B1"));

        Assert.True(commit2.Success);
        Assert.Equal(1, commit2.QuestionsCommitted);

        var examQuestions = await service.GetExamQuestionsAsync(exam.ExamId);
        var questions = examQuestions.Groups.Where(g => g.SectionId == secR).SelectMany(g => g.Questions).OrderBy(q => q.DisplayOrder).ToList();
        Assert.Equal(2, questions.Count);
        Assert.Equal("Initial question stem", questions[0].Stem);
        Assert.Equal(1, questions[0].DisplayOrder);
        Assert.Equal("Second question stem", questions[1].Stem);
        Assert.Equal(2, questions[1].DisplayOrder);
    }

    [Fact]
    public async Task CommitQuickIngest_ReplaceMode_ReplacesExistingQuestionsAtomically()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"QI_REP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Quick Ingest Replace {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        // #266 / INV-6: an imported item is demo stock, so only a demo paper binds it.
        await database.SqlAsync($"UPDATE Exams SET ContentTier = 'demo' WHERE ExamId = {exam.ExamId}");

        var qOld = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Old question to be replaced",
            Explanation: null,
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "Old A", true),
                new QuickIngestParsedOptionDto("B", "Old B", false),
                new QuickIngestParsedOptionDto("C", "Old C", false),
                new QuickIngestParsedOptionDto("D", "Old D", false)
            ],
            Issues: [],
            IsValid: true);

        await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "append",
            Questions: [qOld], CefrLevel: "B1"));

        // Replace with new question
        var qNew = new QuickIngestParsedQuestionDto(
            Index: 1,
            StartLineNumber: 1,
            EndLineNumber: 5,
            Stem: "Brand new replacement question",
            Explanation: "New explanation",
            CategoryCode: null,
            Options:
            [
                new QuickIngestParsedOptionDto("A", "New A", false),
                new QuickIngestParsedOptionDto("B", "New B", true),
                new QuickIngestParsedOptionDto("C", "New C", false),
                new QuickIngestParsedOptionDto("D", "New D", false)
            ],
            Issues: [],
            IsValid: true);

        var replaceCommit = await service.CommitQuickIngestAsync(exam.ExamId, new QuickIngestCommitRequest(
            SectionId: secR,
            Mode: "replace",
            Questions: [qNew], CefrLevel: "B1"));

        Assert.True(replaceCommit.Success);

        var examQuestions = await service.GetExamQuestionsAsync(exam.ExamId);
        var questions = examQuestions.Groups.Where(g => g.SectionId == secR).SelectMany(g => g.Questions).ToList();
        Assert.Single(questions);
        Assert.Equal("Brand new replacement question", questions[0].Stem);
        Assert.Equal(1, questions[0].DisplayOrder);
    }

    /// <summary>
    /// #265: the Studio's read - the section tree, its groups, the cards, and the audit the rail shows.
    /// <para>
    /// #269: the fixture was written by the deleted question door. It is a pool position now, and the
    /// claim is unchanged: one authored position reads back as one group of one card carrying its stem,
    /// beside a populated audit.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetExamStudioAsync_ReturnsSectionsGroupsQuestionsAndAudit()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"STUDIO_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Studio Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, secR, "reading", "Studio Question 1", displayOrder: 1);
        }

        var studio = await service.GetExamStudioAsync(exam.ExamId);

        Assert.NotNull(studio);
        Assert.Equal(exam.ExamId, studio.ExamId);
        Assert.NotEmpty(studio.Sections);
        Assert.NotNull(studio.Audit);

        var rSection = studio.Sections.FirstOrDefault(s => s.SectionId == secR);
        Assert.NotNull(rSection);
        Assert.Single(rSection.Groups);
        Assert.Single(rSection.Groups[0].Questions);
        Assert.Equal("Studio Question 1", rSection.Groups[0].Questions[0].Stem);
    }

    /// <summary>
    /// #265: the up/down buttons rewrite a section's ranks, and the rank is the paper's own order.
    /// <para>
    /// #269: the entries name roster ids, which is what the editor is given. The assertion is the one
    /// this test always made - the moved position is first with rank 1 and its sibling second with rank
    /// 2 - read from the roster, because that is the column the write touches.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReorderSectionItemsAsync_UpdatesDisplayOrder()
    {
        var (certId, _, secR, _, _) = await SeedCertificateWithSectionsAsync($"REORDER_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Reorder Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        PoolFixture.Position q1, q2;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            q1 = await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, secR, "reading", "Question 1", displayOrder: 1);
            q2 = await PoolFixture.AddMcqAsync(db, exam.ExamId, certId, secR, "reading", "Question 2", displayOrder: 2);
        }

        // Reorder q2 first, q1 second
        await service.ReorderSectionItemsAsync(exam.ExamId, new ReorderSectionItemsRequest(
            SectionId: secR,
            Items:
            [
                new ReorderSectionItemEntry(null, q2.ExamItemId, 1),
                new ReorderSectionItemEntry(null, q1.ExamItemId, 2)
            ]));

        await using var verify = DbContextFactory.Create(database.ConnectionString);
        var after = await verify.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId && i.SectionId == secR)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync();

        Assert.Equal([q2.ExamItemId, q1.ExamItemId], after.Select(i => i.ExamItemId));
        Assert.Equal([1, 2], after.Select(i => i.DisplayOrder));
    }

    [Fact]
    public async Task CreateExamClusterAsync_P3Conversation_CreatesGroupAnd3ChildQuestions()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"CLUSTER_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Cluster Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var cluster = await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: null,
            Transcript: "M: Hello Sarah\nW: Hi Bob",
            AudioPath: "/audio/p3-test.mp3",
            AudioStartMs: 1000,
            AudioEndMs: 35000,
            QuestionCount: 3, CefrLevelCode: "B1"));

        Assert.NotNull(cluster);
        Assert.Equal("M: Hello Sarah\nW: Hi Bob", cluster.Transcript);
        Assert.Equal("/audio/p3-test.mp3", cluster.AudioPath);
        Assert.Equal(1000, cluster.AudioStartMs);
        Assert.Equal(35000, cluster.AudioEndMs);
        Assert.Equal(3, cluster.Questions.Count);
        foreach (var q in cluster.Questions)
        {
            Assert.Equal(4, q.Options.Count);
            Assert.Contains(q.Options, o => o.Label == "A" && o.IsCorrect);
        }
    }

    [Fact]
    public async Task CreateExamClusterAsync_SpeakingWriting_SetsFreeResponseAndTimers()
    {
        var (certId, _, _, secS, secW) = await SeedCertificateWithSectionsAsync($"SW_CLUS_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Speaking Cluster {Guid.NewGuid():N}"[..25],
            30,
            ExamPurposeDto.SpeakingPractice));

        var cluster = await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secS,
            QuestionCount: 1, CefrLevelCode: "B1"));

        Assert.NotNull(cluster);
        Assert.Single(cluster.Questions);
        var q = cluster.Questions[0];
        Assert.Equal(QuestionTypeDto.FreeResponse, q.QuestionType);
        Assert.Empty(q.Options);
        Assert.Equal(15, q.PrepSeconds);
        Assert.Equal(45, q.ResponseSeconds);
    }

    [Fact]
    public async Task ExportExamPackageAsync_JsonFormat_ReturnsManifestStream()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"EXPJ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Export Test {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: "Export passage",
            QuestionCount: 2, CefrLevelCode: "B1"));

        var (stream, contentType, fileName) = await service.ExportExamPackageAsync(exam.ExamId, "json");

        Assert.Equal("application/json", contentType);
        Assert.EndsWith("_manifest.json", fileName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal(exam.Name, root.GetProperty("exam").GetProperty("title").GetString());
        Assert.True(root.GetProperty("sections").GetArrayLength() > 0);
        var firstSec = root.GetProperty("sections")[0];
        Assert.True(firstSec.GetProperty("groups").GetArrayLength() > 0);
        Assert.Equal(2, firstSec.GetProperty("groups")[0].GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public async Task ExportExamPackageAsync_ZipFormat_ContainsManifestEntry()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"EXPZ_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId,
            $"Export Zip {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        await service.CreateExamClusterAsync(exam.ExamId, new CreateExamClusterRequest(
            SectionId: secL,
            Passage: "Zip passage",
            QuestionCount: 1, CefrLevelCode: "B1"));

        var (stream, contentType, fileName) = await service.ExportExamPackageAsync(exam.ExamId, "zip");

        Assert.Equal("application/zip", contentType);
        Assert.EndsWith("_package.zip", fileName);
        Assert.NotNull(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        using var entryStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(entryStream);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal(exam.Name, root.GetProperty("exam").GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetExamsAsync_FiltersByPurpose_AndScopesAudioStatus()
    {
        var (certId, secL, secR, secS, secW) = await SeedCertificateWithSectionsAsync($"PURP_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();

        var mockExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Mock Exam {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));

        var placementExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Placement Exam {Guid.NewGuid():N}"[..25], 45, ExamPurposeDto.Placement));

        var speakingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Speaking Exam {Guid.NewGuid():N}"[..25], 20, ExamPurposeDto.SpeakingPractice));

        var writingExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Writing Exam {Guid.NewGuid():N}"[..25], 30, ExamPurposeDto.WritingPractice));

        // 1. Filter by "mock"
        var mockResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "mock", page: 1, pageSize: 20);
        Assert.Single(mockResult.Exams);
        Assert.Equal(mockExam.ExamId, mockResult.Exams[0].ExamId);
        Assert.Equal("No audio", mockResult.Exams[0].AudioStatus); // Listening section exists but no audio uploaded

        // 2. Filter by "placement"
        var placementResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "placement", page: 1, pageSize: 20);
        Assert.Single(placementResult.Exams);
        Assert.Equal(placementExam.ExamId, placementResult.Exams[0].ExamId);

        // 3. Filter by "practice" returns both speaking and writing
        var practiceResult = await service.GetExamsAsync(q: null, status: null, certId: certId, purpose: "practice", page: 1, pageSize: 20);
        Assert.Equal(2, practiceResult.Exams.Count);
        Assert.Contains(practiceResult.Exams, e => e.ExamId == speakingExam.ExamId);
        Assert.Contains(practiceResult.Exams, e => e.ExamId == writingExam.ExamId);

        // Verify audioStatus scoping: Speaking and Writing practice exams are N/A
        var speakingItem = practiceResult.Exams.Single(e => e.ExamId == speakingExam.ExamId);
        var writingItem = practiceResult.Exams.Single(e => e.ExamId == writingExam.ExamId);
        Assert.Equal("N/A", speakingItem.AudioStatus);
        Assert.Equal("N/A", writingItem.AudioStatus);
    }

    /// <summary>
    /// #75 / #265: a section's master track must be sliced per group, or every group plays the whole
    /// audio. #269: the groups are two pool stimuli - each with its own passage, so the audit reads them
    /// as two blocks - and the slice is written through the Studio's group door, which is the only
    /// writer a group has now.
    /// </summary>
    [Fact]
    public async Task Audit_blocks_publish_when_a_listening_section_master_track_is_not_sliced_per_group()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"AUD75_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Audio Audit {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));

        int g1, g2;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var first = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, certId, secL, "listening", "First exchange.", displayOrder: 1,
                passage: "The first announcement.");
            var second = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, certId, secL, "listening", "Second exchange.", displayOrder: 2,
                passage: "The second announcement.");

            g1 = await db.Items.Where(i => i.ItemId == first.ItemId).Select(i => i.StimulusId!.Value).SingleAsync();
            g2 = await db.Items.Where(i => i.ItemId == second.ItemId).Select(i => i.StimulusId!.Value).SingleAsync();
        }

        await database.SqlAsync($"""
            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
            VALUES ({exam.ExamId}, {secL}, {certId}, N'/media/exams/{exam.ExamId}/master.mp3', 600, 1000);
            """);

        // Section audio alone used to pass; every cluster would play the whole master track.
        var unsliced = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.Equal(
            [g1, g2],
            unsliced.Issues.Where(i => i.Code == "GRP_AUDIO_UNSLICED" && i.Severity == "error").Select(i => i.GroupId!.Value).Order());

        await service.UpdateExamStudioGroupAsync(exam.ExamId, g1, new UpdateQuestionGroupRequest(
            secL, Passage: "The first announcement.", AudioStartMs: 0, AudioEndMs: 4000, DisplayOrder: 1));
        await service.UpdateExamStudioGroupAsync(exam.ExamId, g2, new UpdateQuestionGroupRequest(
            secL, Passage: "The second announcement.", AudioStartMs: 4000, AudioEndMs: 9000, DisplayOrder: 2));

        var sliced = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(sliced.Issues, i => i.Code is "GRP_AUDIO_UNSLICED" or "SEC_AUDIO_MISSING");
    }

    /// <summary>
    /// #75: a group's own audio file is silently overridden by the section's master track, which is a
    /// warning rather than a refusal. #269: the group is a pool stimulus, written through the Studio's
    /// group door.
    /// </summary>
    [Fact]
    public async Task Audit_warns_when_a_section_master_track_overrides_a_groups_own_audio_file()
    {
        var (certId, secL, _, _, _) = await SeedCertificateWithSectionsAsync($"AUDOV_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Audio Override {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));

        int own;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, certId, secL, "listening", "The depot exchange.", displayOrder: 1,
                passage: "A short announcement.");
            own = await db.Items.Where(i => i.ItemId == position.ItemId).Select(i => i.StimulusId!.Value).SingleAsync();
        }

        // The group's own file, which the master track then overrides.
        await service.UpdateExamStudioGroupAsync(exam.ExamId, own, new UpdateQuestionGroupRequest(
            secL, Passage: "A short announcement.", AudioPath: "/audio/own-file.mp3"));

        await database.SqlAsync($"""
            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes)
            VALUES ({exam.ExamId}, {secL}, {certId}, N'/media/exams/{exam.ExamId}/master.mp3', 600, 1000);
            """);

        var audit = await service.AuditExamPublishReadinessAsync(exam.ExamId);

        var issue = Assert.Single(audit.Issues, i => i.Code == "GRP_AUDIO_OVERRIDDEN");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal(own, issue.GroupId);

        // A group whose own file is the master track itself (seeded exams) is not overridden.
        await service.UpdateExamStudioGroupAsync(exam.ExamId, own, new UpdateQuestionGroupRequest(
            secL, Passage: "A short announcement.", AudioPath: $"/media/exams/{exam.ExamId}/master.mp3"));
        var same = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(same.Issues, i => i.Code == "GRP_AUDIO_OVERRIDDEN");
    }

    /// <summary>
    /// #192 / #226: a writing task's rubric is its section's criteria, and its word window is the
    /// version's. The audit names both gaps and stops naming them once they are filled.
    /// <para>
    /// #269: the task is a pool position, and filling the word window is a copy-on-write edit - the
    /// scored column is immutable by trigger, so the fix is the next version the binding then pins,
    /// which is exactly what <c>IAdminItemService.UpdateAsync</c> would do. Scoring criteria are
    /// <c>SectionCriteria</c> rows, which the rubric studio writes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FreeResponseQuestions_SaveSectionRubric_AndAuditWordBounds()
    {
        var (certId, _, secR, _, secW) = await SeedCertificateWithSectionsAsync($"RUB_{Guid.NewGuid():N}"[..18]);
        var service = CreateService();
        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Rubric Audit {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.WritingPractice));

        await database.SqlAsync($"""
            DECLARE @skillId INT = (SELECT SkillId FROM Sections WHERE SectionId = {secW});
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder)
            VALUES ({certId}, @skillId, 'TASK', N'Task achievement', 9, 1, 1);
            """);

        PoolFixture.Position position;
        int criterionId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            criterionId = await db.GradingCriteria
                .Where(c => c.CertId == certId && c.Code == "TASK")
                .Select(c => c.CriterionId)
                .SingleAsync();

            position = await PoolFixture.AddProductiveAsync(
                db, exam.ExamId, certId, secW, "writing", "Write a response to the prompt.",
                displayOrder: 1, prepSeconds: 60, responseSeconds: 1200);
        }

        var incompleteAudit = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.Contains(incompleteAudit.Issues, i => i.Code == "Q_FR_CRITERIA_MISSING" && i.QuestionId == position.ExamItemId);
        Assert.Contains(incompleteAudit.Issues, i => i.Code == "Q_FR_WORD_BOUNDS" && i.QuestionId == position.ExamItemId);

        // The rubric half: the section names the criterion its tasks are graded against.
        await database.SqlAsync($"""
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId)
            SELECT {secW}, {criterionId}, SkillId FROM Sections WHERE SectionId = {secW};
            """);

        // The word-window half: a scored change forks the version, and the position pins the new one.
        // Written here rather than through a service because the binding move has no public writer -
        // AdminExamService.UpdatePoolPositionAsync (the Studio autosave) is private and called by
        // nothing since #265 removed the route. The fork itself is CNT-43's rule, and
        // AdminItemAuthoringTests pins it at the door that does own it.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var current = await db.ItemVersions.SingleAsync(v => v.ItemVersionId == position.ItemVersionId);
            var next = new ItemVersion
            {
                ItemId = current.ItemId,
                VersionNumber = current.VersionNumber + 1,
                Stem = current.Stem,
                PrepSeconds = current.PrepSeconds,
                ResponseSeconds = current.ResponseSeconds,
                MinWords = 100,
                MaxWords = 250,
                Explanation = current.Explanation,
            };
            db.ItemVersions.Add(next);
            await db.SaveChangesAsync();

            var binding = await db.ExamItems.SingleAsync(i => i.ExamItemId == position.ExamItemId);
            binding.ItemVersionId = next.ItemVersionId;
            await db.SaveChangesAsync();
        }

        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var studioQuestion = Assert.Single(studio.Sections
            .SelectMany(s => s.Groups)
            .SelectMany(g => g.Questions));
        Assert.Contains(studioQuestion.Criteria, c => c.CriterionId == criterionId);
        Assert.Contains(studio.AvailableCriteria, c => c.CriterionId == criterionId && c.SkillId == studioQuestion.SkillId);

        var completedAudit = await service.AuditExamPublishReadinessAsync(exam.ExamId);
        Assert.DoesNotContain(completedAudit.Issues, i => i.Code is "Q_FR_CRITERIA_MISSING" or "Q_FR_WORD_BOUNDS");

        // An objective position is never asked for a rubric: the check is about free responses.
        var mcqExam = await service.CreateExamAsync(new CreateAdminExamRequest(
            certId, $"Rubric MCQ {Guid.NewGuid():N}"[..25], 60, ExamPurposeDto.Mock));
        PoolFixture.Position mcq;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            mcq = await PoolFixture.AddMcqAsync(
                db, mcqExam.ExamId, certId, secR, "reading", "Choose the correct answer.", displayOrder: 1);
        }

        var afterMcqAudit = await service.AuditExamPublishReadinessAsync(mcqExam.ExamId);
        Assert.DoesNotContain(afterMcqAudit.Issues, i => i.Code == "Q_FR_CRITERIA_MISSING" && i.QuestionId == mcq.ExamItemId);
    }

    [Fact]
    public async Task GetExamStudioAsync_PopulatesDynamicSectionBlueprintModels()
    {
        var certCode = $"BP_{Guid.NewGuid():N}"[..18];
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = $"SK_{cert.CertId}", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var section = new Section
        {
            SkillId = skill.SkillId,
            CertId = cert.CertId,
            Modality = "reading",
            Code = "SEC_BP",
            Name = "Blueprint Section",
            DisplayOrder = 1,
            ExpectedQuestionCount = 10,
            OptionCount = 4,
            ClusterSize = 3,
            UnitCount = 2,
            HideOptionText = true,
            RequirePassage = true,
            RequireAudio = false,
            RequireImage = true,
            RequiredKeywordCount = 3,
            AllowedComponentTypesJson = """["mcq","multi_select","inline_completion"]""",
            LayoutType = "SPLIT_HORIZONTAL",
            NavigationMode = "UNIT_LOCKED_SANDBOX",
            TimerPolicyJson = """{"perSectionSec":1800}""",
            ToolsConfigJson = """{"calculator":false}"""
        };
        db.Sections.Add(section);
        await db.SaveChangesAsync();

        var service = CreateService();

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId,
            $"Blueprint Exam {Guid.NewGuid():N}"[..25],
            60,
            ExamPurposeDto.Mock));

        var studio = await service.GetExamStudioAsync(exam.ExamId);
        var secDto = Assert.Single(studio.Sections);

        Assert.Equal("SEC_BP", secDto.Code);
        Assert.Equal(10, secDto.ExpectedQuestionCount);
        Assert.Equal(4, secDto.OptionCount);
        Assert.Equal(3, secDto.ClusterSize);
        Assert.Equal(2, secDto.UnitCount);
        Assert.True(secDto.HideOptionText);
        Assert.True(secDto.RequirePassage);
        Assert.False(secDto.RequireAudio);
        Assert.True(secDto.RequireImage);
        Assert.Equal(3, secDto.RequiredKeywordCount);
        Assert.Equal("SPLIT_HORIZONTAL", secDto.LayoutType);
        Assert.Equal("UNIT_LOCKED_SANDBOX", secDto.NavigationMode);
        Assert.Equal("""{"perSectionSec":1800}""", secDto.TimerPolicyJson);
        Assert.Equal("""{"calculator":false}""", secDto.ToolsConfigJson);
        Assert.Equal("Reading", secDto.SkillName);
    }

    /// <summary>
    /// #226: the blueprint checks the rail shows - cluster size, required passage, required image, and
    /// the section's declared count - are judged against the paper's positions, and clear when the
    /// paper answers them.
    /// <para>
    /// #269: the fixture is pool stock. A blueprint'd section needs a bundle, so the two children share
    /// one stimulus and each carries an evidence span (INV-2 refuses a child with none); the clearance
    /// rows are INV-3's, so <c>CanPublish</c> is a real verdict rather than "no error yet".
    /// </para>
    /// </summary>
    [Fact]
    public async Task AuditExamPublishReadiness_DynamicBlueprintValidation()
    {
        var certCode = $"AUD_{Guid.NewGuid():N}"[..18];
        int certId, examId, sectionId;
        var itemIds = new List<int>();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            var skill = new Skill
            {
                CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
                DisplayOrder = 1, DurationMinutes = 60,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                SkillId = skill.SkillId,
                CertId = cert.CertId,
                Modality = "reading",
                Code = "SEC_AUD",
                Name = "Audit Section",
                DisplayOrder = 1,
                ExpectedQuestionCount = 2,
                OptionCount = 2,
                ClusterSize = 2,
                RequirePassage = true,
                RequireImage = true,
                AllowedComponentTypesJson = """["mcq"]"""
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId,
                Name = $"Audit Test Exam {Guid.NewGuid():N}"[..25],
                Status = "draft",
                DurationMinutes = 60,
                Purpose = "mock",
                QuestionCount = 2,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();

            // The bundle the section's cluster of two is drawn from. One child is bound, which is what
            // the cluster-size mismatch reports.
            var stimulus = new Stimulus { CefrLevel = CefrLevel.B1, DisplayOrder = 1 };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync();

            var first = await AddSpannedMcqAsync(db, exam, section, stimulus, "Question 1", 1, 10, 40);
            itemIds.Add(first.ItemId);

            certId = cert.CertId;
            examId = exam.ExamId;
            sectionId = section.SectionId;
        }

        var service = CreateService();

        // 1. Cluster size mismatch (requires 2 questions per cluster, but only 1 added)
        // AND RequirePassage missing, AND RequireImage missing
        var audit1 = await service.AuditExamPublishReadinessAsync(examId);
        Assert.False(audit1.CanPublish);
        Assert.Contains(audit1.Issues, i => i.Code == "SEC_QUESTION_COUNT_LOW");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_CLUSTER_SIZE_MISMATCH");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_PASSAGE_MISSING");
        Assert.Contains(audit1.Issues, i => i.Code == "GRP_IMAGE_MISSING");

        // 2. Fix the group: its passage, image, image alt and its second child.
        int stimulusId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var exam = await db.Exams.SingleAsync(e => e.ExamId == examId);
            var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId);
            stimulusId = await db.Items.Where(i => i.ItemId == itemIds[0]).Select(i => i.StimulusId!.Value).SingleAsync();

            var stimulus = await db.Stimuli.SingleAsync(s => s.StimulusId == stimulusId);
            stimulus.Passage = "Reading passage stimulus text here, long enough for two spans.";
            stimulus.ImagePath = "/media/photo.jpg";
            stimulus.ImageAlt = "Photo of office workers";
            await db.SaveChangesAsync();

            var second = await AddSpannedMcqAsync(db, exam, section, stimulus, "Question 2", 2, 50, 80);
            itemIds.Add(second.ItemId);
        }

        await ClearItemsForPublishAsync(certId, itemIds);

        var audit2 = await service.AuditExamPublishReadinessAsync(examId);
        Assert.True(audit2.CanPublish, string.Join("; ", audit2.Issues.Select(i => i.Message)));
    }
}
