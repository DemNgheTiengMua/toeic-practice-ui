using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SavedQuestionServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (SavedQuestionService SavedQuestions, PracticeService Practice, LearnerService Learner) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learnerService = new LearnerService(db);
        var practiceService = new PracticeService(db, learnerService);
        var savedQuestionService = new SavedQuestionService(db, learnerService);
        return (savedQuestionService, practiceService, learnerService);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// A certificate with a paper of three pool positions, plus the section they sit in.
    /// <para>
    /// #269: the bank is pool-native, so a link names an item and the version it pinned. A fixture that
    /// still authored legacy rows would be testing a table that no longer exists.
    /// </para>
    /// </summary>
    private async Task<(int CertId, int SectionId, List<int> PositionIds, List<int> ItemIds, List<int> WrongOptionIds)> SeedCertificateWithPositionsAsync(string certCode)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var skill = new Skill { CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading", DisplayOrder = 1 };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();

        var readSec = new Section { CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading", Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1, OptionCount = 4, ExpectedQuestionCount = 3 };
        db.Sections.Add(readSec);
        await db.SaveChangesAsync();

        var exam = new Exam { CertId = cert.CertId, Name = $"Exam {certCode}", Status = "published", DurationMinutes = 120, Purpose = "mock" };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var positionIds = new List<int>();
        var itemIds = new List<int>();
        var wrongOptionIds = new List<int>();
        for (var i = 1; i <= 3; i++)
        {
            // Key 'A', so the second option id (PoolOptionIndex 1) is the wrong one.
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, readSec.SectionId, "reading",
                stem: $"Stem {i}", displayOrder: i, keyLabel: "A", hint: $"Hint {i}",
                explanation: $"Explanation {i}");
            positionIds.Add(position.ExamItemId);
            itemIds.Add(position.ItemId);
            wrongOptionIds.Add(position.ItemVersionId * 10 + 2);
        }

        return (cert.CertId, readSec.SectionId, positionIds, itemIds, wrongOptionIds);
    }


    /// <summary>
    /// #224: one certificate (so a learner has an active one) and one reading topic with two active pool
    /// items, each with version 1. Nothing names an exam, so a pool link cannot pass by reading a paper.
    /// </summary>
    private async Task<(int CertId, int TopicId, int ItemId, int ItemVersionId)> SeedPoolCellAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Pool Cert', 1);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM')
              INSERT INTO Topics (Modality, Code, Name)
              VALUES ('reading', 'R-GRAM', N'Morphosyntactic structure and lexical precision');

            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            SELECT 'reading', TopicId, 'B1', 'sentence_cloze', 'production', 'active', '{certCode}-pool'
            FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM';

            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            SELECT ItemId, 1, N'The original pool stem, as saved.',
                   N'original-a', N'original-b', N'original-c', N'original-d', 'B', N'Original explanation.'
            FROM Items WHERE BatchTag = '{certCode}-pool';
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var item = await db.Items
            .Where(i => i.BatchTag == $"{certCode}-pool")
            .Select(i => new { i.ItemId, i.TopicId })
            .SingleAsync();
        var versionId = await db.ItemVersions
            .Where(v => v.ItemId == item.ItemId && v.VersionNumber == 1)
            .Select(v => v.ItemVersionId)
            .SingleAsync();

        return (certId, item.TopicId, item.ItemId, versionId);
    }

    [Fact]
    public async Task SaveQuestion_is_idempotent_when_called_multiple_times()
    {
        var certCode = Unique("SQ");
        var (_, _, positions, _, _) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var positionId = positions[0];

        // First call files the position
        var res1 = await savedQuestions.SaveQuestionAsync(userId, positionId, SavedQuestionSource.Manual);
        Assert.True(res1.IsSaved);
        Assert.Equal(SavedQuestionSource.Manual, res1.Source);
        Assert.NotNull(res1.SavedAt);
        Assert.NotNull(res1.SavedQuestionId);

        // Second call on the same position succeeds idempotently without error
        var res2 = await savedQuestions.SaveQuestionAsync(userId, positionId, SavedQuestionSource.Manual);
        Assert.True(res2.IsSaved);
        Assert.Equal(SavedQuestionSource.Manual, res2.Source);
        Assert.Equal(res1.SavedQuestionId, res2.SavedQuestionId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var rows = await db.SavedQuestions.Where(sq => sq.UserId == userId).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task UnsaveQuestion_is_idempotent_when_called_for_unsaved_question()
    {
        var certCode = Unique("SQ");
        var (_, _, positions, _, _) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var positionId = positions[0];

        // Unsaving something that is NOT currently saved succeeds idempotently
        var res1 = await savedQuestions.UnsaveAsync(userId, 999_999);
        Assert.False(res1.IsSaved);

        // Save it first
        var saved = await savedQuestions.SaveQuestionAsync(userId, positionId, SavedQuestionSource.Manual);
        Assert.True(await savedQuestions.IsQuestionSavedAsync(userId, positionId));

        // Unsave it
        var res2 = await savedQuestions.UnsaveAsync(userId, saved.SavedQuestionId!.Value);
        Assert.False(res2.IsSaved);
        Assert.False(await savedQuestions.IsQuestionSavedAsync(userId, positionId));

        // Second unsave succeeds idempotently
        var res3 = await savedQuestions.UnsaveAsync(userId, saved.SavedQuestionId!.Value);
        Assert.False(res3.IsSaved);
    }

    [Fact]
    public async Task SaveQuestion_promotes_incorrect_source_to_manual_when_manually_saved()
    {
        var certCode = Unique("SQ");
        var (_, _, positions, _, _) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();
        var positionId = positions[0];

        // Initially saved as incorrect
        var saved = await savedQuestions.SaveQuestionAsync(userId, positionId, SavedQuestionSource.Incorrect);
        var view1 = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.Equal(SavedQuestionSource.Incorrect, view1.Source);

        // Later learner explicitly saves it manually
        await savedQuestions.SaveQuestionAsync(userId, positionId, SavedQuestionSource.Manual);
        var view2 = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.Equal(SavedQuestionSource.Manual, view2.Source);
    }

    [Fact]
    public async Task GetSavedQuestions_lists_only_learner_own_items_for_active_certificate()
    {
        var certA = Unique("CA");
        var certB = Unique("CB");
        var (_, _, positionsA, itemsA, _) = await SeedCertificateWithPositionsAsync(certA);
        var (_, _, positionsB, itemsB, _) = await SeedCertificateWithPositionsAsync(certB);

        var user1 = await SeedLearnerAsync(certA);
        var user2 = await SeedLearnerAsync(certA);
        var (savedQuestions, _, learners) = CreateServices();

        // User1 saves position 1 from CertA
        await savedQuestions.SaveQuestionAsync(user1, positionsA[0], SavedQuestionSource.Manual);
        // User1 switches to CertB to save position 1 from CertB, then switches back to CertA
        await learners.SwitchCertificateAsync(user1, certB);
        await savedQuestions.SaveQuestionAsync(user1, positionsB[0], SavedQuestionSource.Manual);
        await learners.SwitchCertificateAsync(user1, certA);

        // User2 saves position 2 from CertA
        await savedQuestions.SaveQuestionAsync(user2, positionsA[1], SavedQuestionSource.Manual);

        // User1 viewing CertA should only see User1's item from CertA
        var viewUser1CertA = await savedQuestions.GetSavedQuestionsAsync(user1, new SavedQuestionFilterInput());
        Assert.Single(viewUser1CertA.Questions);
        Assert.Equal(itemsA[0], viewUser1CertA.Questions[0].ItemId);
        Assert.Equal(certA, viewUser1CertA.Certificate.Code);
        Assert.Equal(1, viewUser1CertA.TotalSavedCount);
        Assert.Equal(1, viewUser1CertA.ManualSavedCount);
        Assert.Equal(0, viewUser1CertA.IncorrectSavedCount);

        // User2 viewing CertA should only see User2's item
        var viewUser2CertA = await savedQuestions.GetSavedQuestionsAsync(user2, new SavedQuestionFilterInput());
        Assert.Single(viewUser2CertA.Questions);
        Assert.Equal(itemsA[1], viewUser2CertA.Questions[0].ItemId);

        // User1 switches to CertB
        await learners.SwitchCertificateAsync(user1, certB);
        var viewUser1CertB = await savedQuestions.GetSavedQuestionsAsync(user1, new SavedQuestionFilterInput());
        Assert.Single(viewUser1CertB.Questions);
        Assert.Equal(itemsB[0], viewUser1CertB.Questions[0].ItemId);
        Assert.Equal(certB, viewUser1CertB.Certificate.Code);
    }

    [Fact]
    public async Task GetSavedQuestions_filters_by_source()
    {
        var certCode = Unique("SQ");
        var (_, _, positions, items, _) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        await savedQuestions.SaveQuestionAsync(userId, positions[0], SavedQuestionSource.Manual);
        await savedQuestions.SaveQuestionAsync(userId, positions[1], SavedQuestionSource.Incorrect);

        // Filter by manual
        var manualOnly = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput(Source: SavedQuestionSource.Manual));
        Assert.Single(manualOnly.Questions);
        Assert.Equal(items[0], manualOnly.Questions[0].ItemId);
        Assert.Equal(2, manualOnly.TotalSavedCount); // Total overall for cert remains 2
        Assert.Equal(1, manualOnly.ManualSavedCount);
        Assert.Equal(1, manualOnly.IncorrectSavedCount);

        // Filter by incorrect
        var incorrectOnly = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput(Source: SavedQuestionSource.Incorrect));
        Assert.Single(incorrectOnly.Questions);
        Assert.Equal(items[1], incorrectOnly.Questions[0].ItemId);
    }

    [Fact]
    public async Task GetSavedQuestion_returns_options_explanation_and_media()
    {
        var certCode = Unique("SQ");
        var (_, _, positions, itemIds, _) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var saved = await savedQuestions.SaveQuestionAsync(userId, positions[0], SavedQuestionSource.Manual);

        var item = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.Equal(itemIds[0], item.ItemId);
        Assert.Equal("Stem 1", item.Stem);
        Assert.Equal("Explanation 1", item.Explanation);
        Assert.Equal("Hint 1", item.Hint);
        // A version stores four labels rather than option rows, so all four come back.
        Assert.Equal(4, item.Options.Count);
        Assert.Contains(item.Options, o => o.Label == "A" && o.IsCorrect);
        Assert.Contains(item.Options, o => o.Label == "B" && !o.IsCorrect);
    }

    [Fact]
    public async Task GetSavedQuestion_throws_when_not_saved_by_user()
    {
        var certCode = Unique("SQ");
        await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.GetSavedQuestionAsync(userId, 999_999));
        Assert.Equal(SavedQuestionException.SavedQuestionNotFound, ex.Code);
    }

    [Fact]
    public async Task SubmitAnswer_automatically_saves_incorrect_mcq_to_saved_questions()
    {
        var certCode = Unique("SQ");
        var (_, sectionId, positions, items, wrongOpts) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));
        var versionId = positions[0] * 0 + await VersionIdAtAsync(positions[0]);

        // Submit wrong answer
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, versionId, new SubmitPracticeAnswerInput(wrongOpts[0]));
        Assert.False(result.IsCorrect);
        Assert.True(result.IsSaved);
        Assert.NotNull(result.SavedQuestionId);

        // Verify the item now exists in SavedQuestions with source = 'incorrect'
        var savedItem = await savedQuestions.GetSavedQuestionAsync(userId, result.SavedQuestionId!.Value);
        Assert.Equal(items[0], savedItem.ItemId);
        Assert.Equal(SavedQuestionSource.Incorrect, savedItem.Source);
    }

    [Fact]
    public async Task SubmitAnswer_does_not_overwrite_manual_source_if_already_saved_manually()
    {
        var certCode = Unique("SQ");
        var (_, sectionId, positions, _, wrongOpts) = await SeedCertificateWithPositionsAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();
        var versionId = await VersionIdAtAsync(positions[0]);

        // Manually save first
        var manual = await savedQuestions.SaveQuestionAsync(userId, positions[0], SavedQuestionSource.Manual);

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(sectionId));

        // Submit wrong answer
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, versionId, new SubmitPracticeAnswerInput(wrongOpts[0]));
        Assert.False(result.IsCorrect);
        Assert.True(result.IsSaved);
        Assert.Equal(manual.SavedQuestionId, result.SavedQuestionId);

        // Source stays manual
        var savedItem = await savedQuestions.GetSavedQuestionAsync(userId, manual.SavedQuestionId!.Value);
        Assert.Equal(SavedQuestionSource.Manual, savedItem.Source);
    }

    [Fact]
    public async Task GetSavedQuestionAsync_throws_when_question_belongs_to_different_certificate_than_active()
    {
        var certCodeA = Unique("SQA");
        var certCodeB = Unique("SQB");
        var (_, _, positionsA, itemsA, _) = await SeedCertificateWithPositionsAsync(certCodeA);
        await SeedCertificateWithPositionsAsync(certCodeB);

        var userId = await SeedLearnerAsync(certCodeA);
        var (savedQuestions, _, learner) = CreateServices();

        // Save a position while cert A is active
        var saved = await savedQuestions.SaveQuestionAsync(userId, positionsA[0], SavedQuestionSource.Manual);

        // Verify it loads when cert A is active
        var item = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.Equal(itemsA[0], item.ItemId);

        // Switch learner's active cert to cert B
        await learner.SwitchCertificateAsync(userId, certCodeB);

        // Certificate isolation: accessing cert A item while in cert B must throw 404 / not found
        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value));
        Assert.Equal(SavedQuestionException.SavedQuestionNotFound, ex.Code);
    }

    [Fact]
    public async Task SaveQuestionAsync_throws_when_question_belongs_to_different_certificate_than_active()
    {
        var certCodeA = Unique("SQA");
        var certCodeB = Unique("SQB");
        var (_, _, positionsA, _, _) = await SeedCertificateWithPositionsAsync(certCodeA);
        await SeedCertificateWithPositionsAsync(certCodeB);

        var userId = await SeedLearnerAsync(certCodeB); // active cert is B
        var (savedQuestions, _, _) = CreateServices();
        var positionA = positionsA[0]; // the position belongs to cert A

        // Saving a position from another certificate must be rejected
        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.SaveQuestionAsync(userId, positionA, SavedQuestionSource.Manual));
        Assert.Equal(SavedQuestionException.QuestionNotFound, ex.Code);
    }

    private async Task<int> VersionIdAtAsync(int examItemId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.ExamItems.Where(i => i.ExamItemId == examItemId).Select(i => i.ItemVersionId).SingleAsync();
    }


    // =============================================================================================
    // #224: a pool link pins the version, and nothing moves it.
    // =============================================================================================

    /// <summary>
    /// AC1: a saved pool question stores the item and the version, and reopening it renders that
    /// version.
    /// <para>
    /// The trap this test is written against is a link that stores <c>ItemVersionId</c> and then renders
    /// the item's <b>latest</b> version: that passes any assertion that only checks the column is
    /// populated. So the test writes a second version with different content and asserts the original
    /// stem and every option come back. A render that reads "the item" rather than "the pin" returns
    /// the corrected text and fails here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_saved_pool_item_renders_the_version_it_pinned_after_a_reversion()
    {
        var certCode = Unique("P224");
        var (_, _, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var saved = await savedQuestions.SaveItemVersionAsync(userId, versionId);
        Assert.True(saved.IsSaved);
        Assert.NotNull(saved.SavedQuestionId);

        // A correction arrives: a new version with a different stem, a different key and different
        // option text. Nothing about the saved link may move.
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = itemId,
                VersionNumber = 2,
                Stem = "The corrected pool stem, which the learner never saw.",
                OptionA = "corrected-a",
                OptionB = "corrected-b",
                OptionC = "corrected-c",
                OptionD = "corrected-d",
                KeyLabel = "C",
                Explanation = "Corrected explanation.",
            });
            await db.SaveChangesAsync();
        }

        var reopened = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);

        Assert.Equal(itemId, reopened.ItemId);
        Assert.Equal(versionId, reopened.ItemVersionId);
        Assert.Equal("The original pool stem, as saved.", reopened.Stem);
        Assert.Equal("Original explanation.", reopened.Explanation);
        Assert.Equal(
            ["original-a", "original-b", "original-c", "original-d"],
            reopened.Options.Select(o => o.Text).ToArray());
        // The key is the pinned version's B, not the corrected version's C.
        Assert.Equal("B", reopened.Options.Single(o => o.IsCorrect).Label);

        // The list projection reads the same pin, not a different one.
        var listed = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput());
        var fromList = Assert.Single(listed.Questions);
        Assert.Equal(versionId, fromList.ItemVersionId);
        Assert.Equal("The original pool stem, as saved.", fromList.Stem);
        Assert.Equal(
            ["original-a", "original-b", "original-c", "original-d"],
            fromList.Options.Select(o => o.Text).ToArray());
    }

    /// <summary>
    /// AC2: a retired item still renders from a saved link. Retiring it is a lifecycle move, not a
    /// deletion, so the version the learner pinned is still readable and is what comes back.
    /// </summary>
    [Fact]
    public async Task A_retired_item_still_renders_from_a_saved_link()
    {
        var certCode = Unique("P224R");
        var (_, _, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var saved = await savedQuestions.SaveItemVersionAsync(userId, versionId);

        // Ordinary pool rotation: active -> retired.
        await database.SqlAsync($"UPDATE Items SET LifecycleStatus = 'retired' WHERE ItemId = {itemId};");

        var reopened = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.Equal("The original pool stem, as saved.", reopened.Stem);
        // Retired is not defective: the key still counts as the key.
        Assert.False(reopened.IsRetiredDefective);
    }

    /// <summary>
    /// AC3: an item withdrawn as defective is <b>flagged</b>, not rendered as normal.
    /// <para>
    /// The trap is a link that renders the pinned version and never reads the item's lifecycle at all:
    /// it passes AC1 and AC2 and silently shows a wrong key here. This asserts the flag is on the
    /// projection <b>and</b> reaches the rendered page (see
    /// <c>tests/WebMVC.Tests/SavedQuestionsControllerTests</c> for the card, which is where a stored-only
    /// flag would be invisible to the learner).
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_item_retired_as_defective_is_flagged_on_the_saved_link()
    {
        var certCode = Unique("P224D");
        var (_, _, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var saved = await savedQuestions.SaveItemVersionAsync(userId, versionId);

        // The item's key was found wrong. quarantined -> retired_defective is the named transition.
        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'quarantined' WHERE ItemId = {itemId};
            UPDATE Items SET LifecycleStatus = 'retired_defective' WHERE ItemId = {itemId};
            """);

        var reopened = await savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value);
        Assert.True(reopened.IsRetiredDefective);
        // Faked out: the content still renders. The flag is the safety notice, not a blank page.
        Assert.Equal("The original pool stem, as saved.", reopened.Stem);

        var listed = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput());
        Assert.True(Assert.Single(listed.Questions).IsRetiredDefective);
    }

    /// <summary>
    /// AC5: a re-versioned item does not change an existing saved link — the same claim as AC1,
    /// asserted on the stored row rather than the projection, so a renderer that happened to cache the
    /// old text could not pass it alone.
    /// </summary>
    [Fact]
    public async Task Re_saving_an_item_does_not_re_pin_it_to_the_newer_version()
    {
        var certCode = Unique("P224RS");
        var (_, _, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, _) = CreateServices();

        var first = await savedQuestions.SaveItemVersionAsync(userId, versionId);

        int secondVersionId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var v2 = new ItemVersion
            {
                ItemId = itemId,
                VersionNumber = 2,
                Stem = "The corrected pool stem.",
                OptionA = "corrected-a",
                OptionB = "corrected-b",
                OptionC = "corrected-c",
                OptionD = "corrected-d",
                KeyLabel = "C",
            };
            db.ItemVersions.Add(v2);
            await db.SaveChangesAsync();
            secondVersionId = v2.ItemVersionId;
        }

        // The learner taps Save again while looking at the new version. It is the same bookmark, and the
        // pin must not move.
        var second = await savedQuestions.SaveItemVersionAsync(userId, secondVersionId);
        Assert.True(second.IsSaved);
        Assert.Equal(first.SavedQuestionId, second.SavedQuestionId);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var row = await db.SavedQuestions.SingleAsync(sq => sq.UserId == userId && sq.ItemId == itemId);
            Assert.Equal(versionId, row.ItemVersionId);
        }

        var reopened = await savedQuestions.GetSavedQuestionAsync(userId, first.SavedQuestionId!.Value);
        Assert.Equal(versionId, reopened.ItemVersionId);
        Assert.Equal("The original pool stem, as saved.", reopened.Stem);
    }

    /// <summary>
    /// A pool link is scoped to the certificate it was saved under. An item is certificate-agnostic
    /// (D2), so without this a VSTEP bookmark would appear in a TOEIC learner's bank.
    /// </summary>
    [Fact]
    public async Task A_pool_link_appears_only_in_the_bank_of_the_certificate_it_was_saved_under()
    {
        var certCode = Unique("P224C");
        var (_, _, _, versionId) = await SeedPoolCellAsync(certCode);
        var otherCertCode = Unique("P224O");
        await database.SqlAsync($"INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{otherCertCode}', N'Other', 1);");

        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, _, learners) = CreateServices();

        var saved = await savedQuestions.SaveItemVersionAsync(userId, versionId);

        var here = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput());
        Assert.Equal(saved.SavedQuestionId, Assert.Single(here.Questions).SavedQuestionId);

        await learners.SwitchCertificateAsync(userId, otherCertCode);
        var elsewhere = await savedQuestions.GetSavedQuestionsAsync(userId, new SavedQuestionFilterInput());
        Assert.Empty(elsewhere.Questions);

        var ex = await Assert.ThrowsAsync<SavedQuestionException>(() =>
            savedQuestions.GetSavedQuestionAsync(userId, saved.SavedQuestionId!.Value));
        Assert.Equal(SavedQuestionException.SavedQuestionNotFound, ex.Code);
    }

    /// <summary>
    /// A wrong answer on a pool item is filed automatically, pinning the version that was answered.
    /// Before #224 the bank was skipped for pool items, so a pool drill's wrong answers were lost.
    /// </summary>
    [Fact]
    public async Task A_wrong_pool_answer_is_filed_pinning_the_answered_version()
    {
        var certCode = Unique("P224W");
        var (_, topicId, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(topicId, CefrLevel.B1));
        var question = (await practice.GetSessionQuestionsAsync(userId, session.SessionId))
            .Single(q => q.ItemVersionId == versionId);
        Assert.False(question.IsSaved);

        // Version 1's key is B; A is wrong.
        var wrongOptionId = versionId * 10 + 1;
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, versionId, new SubmitPracticeAnswerInput(wrongOptionId));

        Assert.False(result.IsCorrect);
        Assert.True(result.IsSaved);
        Assert.NotNull(result.SavedQuestionId);

        var saved = await savedQuestions.GetSavedQuestionAsync(userId, result.SavedQuestionId!.Value);
        Assert.Equal(itemId, saved.ItemId);
        Assert.Equal(versionId, saved.ItemVersionId);
        Assert.Equal(SavedQuestionSource.Incorrect, saved.Source);

        // And the drill now reports it as saved, so the control is not a lie.
        var reread = (await practice.GetSessionQuestionsAsync(userId, session.SessionId))
            .Single(q => q.ItemVersionId == versionId);
        Assert.True(reread.IsSaved);
        Assert.Equal(result.SavedQuestionId, reread.SavedQuestionId);
    }

    /// <summary>A correct pool answer is not filed, and the drill says so.</summary>
    [Fact]
    public async Task A_correct_pool_answer_is_not_filed_in_the_bank()
    {
        var certCode = Unique("P224C2");
        var (_, topicId, itemId, versionId) = await SeedPoolCellAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (savedQuestions, practice, _) = CreateServices();

        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(topicId, CefrLevel.B1));
        var rightOptionId = versionId * 10 + 2; // version 1's key is B
        var result = await practice.SubmitAnswerAsync(userId, session.SessionId, versionId, new SubmitPracticeAnswerInput(rightOptionId));

        Assert.True(result.IsCorrect);
        Assert.False(result.IsSaved);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.SavedQuestions.AnyAsync(sq => sq.UserId == userId && sq.ItemId == itemId));
    }
}
