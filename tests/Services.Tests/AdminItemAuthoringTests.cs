using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #188: admin authoring writes the pool.
/// <para>
/// The fixture authors through the service and then reads the database directly, so no test can pass
/// by trusting the service's own projection of what it wrote.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminItemAuthoringTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AdminItemService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// The canonical topic vocabulary is a closed set of twenty, so a test reuses a canonical topic
    /// rather than inventing one: a synthetic topic accumulates a row per run and eventually pushes
    /// the seed over the ceiling.
    /// </summary>
    private async Task<int> ReadingTopicAsync()
    {
        await using var db = Db();
        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Modality == "reading" && t.Code == "R-DET");
        if (topic is null)
        {
            topic = new Topic { Modality = "reading", Code = "R-DET", Name = "Local factual retrieval and paraphrase matching" };
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
        }

        return topic.TopicId;
    }

    private static CreatePoolItemRequest ReadingItem(int topicId, string stem, string key = "A") =>
        new(
            ModalityDto.Reading,
            topicId,
            "B1",
            "sentence_cloze",
            stem,
            "The depot opens at six.",
            "The night ledger is signed after six.",
            "Only the supervisor opens the gate.",
            "Visitors wait outside until seven.",
            key,
            "The first option restates the passage.",
            // #198: gate one refuses an option with no rationale (CNT-35), and the authoring path
            // invokes it. These four are what an author supplies; a fixture without them is testing a
            // save the gate is meant to refuse.
            RationaleA: "Correct: the passage states the opening time.",
            RationaleB: "The passage never gives a time for the ledger.",
            RationaleC: "The supervisor opens the gate, not the depot.",
            RationaleD: "Waiting outside is what visitors do, not when the depot opens.");

    /// <summary>
    /// AC1: a created item is a pool item plus a version, and never an exam reference. The table
    /// columns are also checked, because "no exam reference" is a schema fact in #184 and a service
    /// that quietly wrote one would need a column to write it into.
    /// </summary>
    [Fact]
    public async Task Creating_an_item_writes_one_pool_item_and_one_version_and_no_exam_reference()
    {
        var topicId = await ReadingTopicAsync();

        var created = await CreateService().CreateAsync(ReadingItem(topicId, "What time does the depot open?"));

        await using var db = Db();
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.ItemId == created.ItemId);
        var versions = await db.ItemVersions.AsNoTracking().Where(v => v.ItemId == created.ItemId).ToListAsync();

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("What time does the depot open?", version.Stem);
        Assert.Equal("A", version.KeyLabel);
        Assert.Equal("B1", item.CefrLevel.Code);
        Assert.Equal("reading", item.Modality);
        Assert.Equal(topicId, item.TopicId);

        // The uglier half of AC1: the database has no exam, section or certificate column to hold a
        // reference, so this is checked rather than assumed.
        var columns = await db.Database
            .SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Items'")
            .ToListAsync();
        Assert.DoesNotContain("ExamId", columns);
        Assert.DoesNotContain("SectionId", columns);
        Assert.DoesNotContain("CertId", columns);

        // #269: the legacy Questions table no longer exists, so "the item write touched no legacy row"
        // is a schema fact now, not something a test can observe.
    }

    [Fact]
    public async Task A_created_item_is_immediately_active_with_target_bands_and_bypasses_gates()
    {
        var topicId = await ReadingTopicAsync();

        var created = await CreateService().CreateAsync(new CreatePoolItemRequest(
            ModalityDto.Reading,
            topicId,
            "B1",
            "sentence_cloze",
            "Which window is the depot's?",
            "North window.",
            "South window.",
            "East window.",
            "West window.",
            "A",
            TargetBands: "b1, b2, 5.5"));

        Assert.Equal("active", created.LifecycleStatus);
        Assert.Equal("B1,B2,5.5", created.TargetBands);

        await using var db = Db();
        var stored = await db.Items.AsNoTracking().SingleAsync(i => i.ItemId == created.ItemId);
        Assert.Equal("active", stored.LifecycleStatus);
        Assert.Equal("B1,B2,5.5", stored.TargetBands);
    }

    /// <summary>
    /// AC2, the criterion that has to be observed rather than asserted: a scored edit forks a version,
    /// the earlier row is byte-identical afterwards, and the item now reads through the new one.
    /// </summary>
    [Fact]
    public async Task Editing_a_scored_field_creates_a_new_version_and_leaves_the_first_identical()
    {
        var topicId = await ReadingTopicAsync();
        var service = CreateService();
        var created = await service.CreateAsync(ReadingItem(topicId, "What time does the depot open?"));

        ItemVersion before;
        await using (var db = Db())
        {
            before = await db.ItemVersions.AsNoTracking().SingleAsync(v => v.ItemVersionId == created.ItemVersionId);
        }

        var edited = await service.UpdateAsync(
            created.ItemId,
            new UpdatePoolItemRequest(
                Stem: "What time does the depot close?",
                OptionA: "Six in the morning.",
                OptionB: "Six in the evening.",
                OptionC: "Eight in the morning.",
                OptionD: "Eight in the evening.",
                KeyLabel: "B",
                Explanation: "The passage gives the closing time.",
                // #198: an edit is the same door as a create, so the version it writes is judged by
                // gate one too. A request that drops the rationales would now be refused.
                RationaleA: created.RationaleA,
                RationaleB: "Correct: six in the evening is the closing time.",
                RationaleC: created.RationaleC,
                RationaleD: created.RationaleD));

        // Two versions exist, and the edit went through the second.
        Assert.Equal(2, edited.VersionCount);
        Assert.Equal(2, edited.VersionNumber);
        Assert.NotEqual(created.ItemVersionId, edited.ItemVersionId);
        Assert.Equal("What time does the depot close?", edited.Stem);

        await using (var db = Db())
        {
            var versions = await db.ItemVersions.AsNoTracking()
                .Where(v => v.ItemId == created.ItemId)
                .OrderBy(v => v.VersionNumber)
                .ToListAsync();
            Assert.Equal(2, versions.Count);

            // Version 1 is byte-identical to what it was: a copy-on-write that mutated the original
            // would make every past attempt that pinned it render the corrected item instead.
            var first = versions[0];
            Assert.Equal(before.ItemVersionId, first.ItemVersionId);
            Assert.Equal(before.VersionNumber, first.VersionNumber);
            Assert.Equal(before.Stem, first.Stem);
            Assert.Equal(before.OptionA, first.OptionA);
            Assert.Equal(before.OptionB, first.OptionB);
            Assert.Equal(before.OptionC, first.OptionC);
            Assert.Equal(before.OptionD, first.OptionD);
            Assert.Equal(before.KeyLabel, first.KeyLabel);
            Assert.Equal(before.Explanation, first.Explanation);
            Assert.Equal(before.EvidenceStart, first.EvidenceStart);
            Assert.Equal(before.EvidenceEnd, first.EvidenceEnd);
            Assert.Equal(before.EvidenceQuote, first.EvidenceQuote);
            Assert.Equal(before.ReferenceText, first.ReferenceText);
            Assert.Equal(before.ReferenceKind, first.ReferenceKind);

            // And the fork is the scored content the caller sent, not a copy of the old row.
            var second = versions[1];
            Assert.Equal("What time does the depot close?", second.Stem);
            Assert.Equal("B", second.KeyLabel);
            Assert.Equal("Six in the evening.", second.OptionB);
        }
    }

    /// <summary>
    /// AC3: an explanation typo is the one in-place edit CNT-43 allows, so it must not fork.
    /// </summary>
    [Fact]
    public async Task Editing_only_the_explanation_does_not_create_a_version()
    {
        var topicId = await ReadingTopicAsync();
        var service = CreateService();
        var created = await service.CreateAsync(ReadingItem(topicId, "What time does the depot open?"));

        var edited = await service.UpdateAsync(
            created.ItemId,
            new UpdatePoolItemRequest(
                Stem: created.Stem,
                OptionA: created.OptionA,
                OptionB: created.OptionB,
                OptionC: created.OptionC,
                OptionD: created.OptionD,
                KeyLabel: created.KeyLabel,
                Explanation: "The first option restates the passage (typo fixed).",
                RationaleA: created.RationaleA,
                RationaleB: created.RationaleB,
                RationaleC: created.RationaleC,
                RationaleD: created.RationaleD));

        Assert.Equal(1, edited.VersionCount);
        Assert.Equal(1, edited.VersionNumber);
        Assert.Equal(created.ItemVersionId, edited.ItemVersionId);
        Assert.Equal("The first option restates the passage (typo fixed).", edited.Explanation);

        await using var db = Db();
        Assert.Equal(1, await db.ItemVersions.CountAsync(v => v.ItemId == created.ItemId));
        // …and the typo actually landed.
        var stored = await db.ItemVersions.AsNoTracking().SingleAsync(v => v.ItemId == created.ItemId);
        Assert.Contains("typo fixed", stored.Explanation);
    }

    /// <summary>
    /// The trap in the ticket: an item on a topic of another skill is refused by the composite foreign
    /// key, which surfaces as a 500. The service refuses it first, and the assertion names the reason
    /// so a test that merely "throws" cannot pass.
    /// </summary>
    [Fact]
    public async Task A_listening_item_on_a_reading_topic_is_refused_with_a_usable_message()
    {
        var readingTopic = await ReadingTopicAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            CreateService().CreateAsync(ReadingItem(readingTopic, "Who signs the ledger?") with
            {
                Modality = ModalityDto.Listening,
                ItemType = "short_exchange",
            }));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("reading topic", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("listening", refused.Message, StringComparison.OrdinalIgnoreCase);

        await using var db = Db();
        Assert.Equal(0, await db.Items.CountAsync(i => i.TopicId == readingTopic && i.Modality == "listening"));
    }

    /// <summary>
    /// D0.4's matrix is on the item, not on the request, so a wrong (modality, type) pair is refused
    /// before the CHECK that would otherwise produce a 500.
    /// </summary>
    [Fact]
    public async Task An_item_type_belonging_to_another_skill_is_refused()
    {
        var topicId = await ReadingTopicAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            CreateService().CreateAsync(ReadingItem(topicId, "Argue for a congestion charge.") with
            {
                ItemType = "persuasive_essay",
            }));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("reading item types", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stimulus and its children in one place: the authoring call creates the shared passage, and
    /// the item points at it rather than duplicating the text.
    /// </summary>
    [Fact]
    public async Task A_stimulus_supplied_with_the_item_is_written_once_and_shared()
    {
        var topicId = await ReadingTopicAsync();

        var created = await CreateService().CreateAsync(
            ReadingItem(topicId, "What must staff do before six?") with
            {
                Stimulus = new CreatePoolStimulusRequest(
                    "B1",
                    Passage: "The depot opens at six. Staff who arrive before then sign the night ledger.",
                    DomainContext: "workplace"),
            });

        Assert.NotNull(created.StimulusId);

        await using var db = Db();
        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == created.StimulusId);
        Assert.Equal("B1", stimulus.CefrLevel.Code);
        Assert.Equal("workplace", stimulus.DomainContext);
        Assert.Contains("night ledger", stimulus.Passage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same stimulus can carry several children: the second item names the first one's stimulus
    /// rather than duplicating the passage, which is the shape D0.3's envelope and the shared-audio
    /// volume both assume.
    /// </summary>
    [Fact]
    public async Task A_second_child_joins_the_first_one_s_stimulus_instead_of_copying_the_passage()
    {
        var topicId = await ReadingTopicAsync();
        var service = CreateService();

        var first = await service.CreateAsync(
            ReadingItem(topicId, "What must staff do before six?") with
            {
                Stimulus = new CreatePoolStimulusRequest("B1", Passage: "The depot opens at six."),
            });

        var second = await service.CreateAsync(
            ReadingItem(topicId, "Who signs the ledger?") with { StimulusId = first.StimulusId });

        Assert.Equal(first.StimulusId, second.StimulusId);

        await using var db = Db();
        Assert.Equal(1, await db.Stimuli.CountAsync(s => s.StimulusId == first.StimulusId));
        Assert.Equal(2, await db.Items.CountAsync(i => i.StimulusId == first.StimulusId));
    }

    /// <summary>
    /// Both halves at once is not a shape: a child either creates its passage or joins one.
    /// </summary>
    [Fact]
    public async Task Naming_a_new_stimulus_and_an_existing_one_at_once_is_refused()
    {
        var topicId = await ReadingTopicAsync();
        var service = CreateService();

        var first = await service.CreateAsync(
            ReadingItem(topicId, "What must staff do before six?") with
            {
                Stimulus = new CreatePoolStimulusRequest("B1", Passage: "The depot opens at six."),
            });

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            service.CreateAsync(
                ReadingItem(topicId, "Who signs the ledger?") with
                {
                    Stimulus = new CreatePoolStimulusRequest("B1", Passage: "A second passage."),
                    StimulusId = first.StimulusId,
                }));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("not both", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A productive task is the other legal version shape: no options and no key. A half-filled
    /// version is the shape CK_ItemVersions_OptionsComplete refuses, and it is refused here as a
    /// sentence rather than as a 500 from the constraint.
    /// </summary>
    [Fact]
    public async Task A_half_filled_option_set_is_refused_before_the_check_constraint()
    {
        var topicId = await ReadingTopicAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            CreateService().CreateAsync(
                new CreatePoolItemRequest(
                    ModalityDto.Reading, topicId, "B1", "passage_comprehension", "A stem with three options.",
                    OptionA: "a", OptionB: "b", OptionC: "c", KeyLabel: "A")));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("four options", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unknown level is refused by the service with the level vocabulary in the message, rather
    /// than by <c>CK_Items_Level</c> as a 500.
    /// </summary>
    [Fact]
    public async Task An_unknown_level_is_refused_and_the_message_names_the_authored_ladder()
    {
        var topicId = await ReadingTopicAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() =>
            CreateService().CreateAsync(ReadingItem(topicId, "Which window is open?") with { CefrLevelCode = "C3" }));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains("A1-C2", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC4's pool half: the second, explicit act puts the authored version on a paper. Before it, the
    /// item is on no paper at all - which is what "authoring writes the pool" means.
    /// </summary>
    [Fact]
    public async Task A_binding_is_the_only_way_an_authored_item_reaches_a_paper()
    {
        var topicId = await ReadingTopicAsync();
        var certCode = Unique("IBN");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Authored Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Authored paper', 'draft', 60, 'mock', 1);
            """);

        int examId;
        int sectionId;
        await using (var db = Db())
        {
            var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
            examId = await db.Exams.Where(e => e.CertId == cert.CertId).Select(e => e.ExamId).SingleAsync();
            sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        }

        var created = await CreateService().CreateAsync(ReadingItem(topicId, "What time does the depot open?"));

        // Nothing on the paper yet: authoring names no exam, so this is the only honest answer.
        await using (var db = Db())
        {
            Assert.Equal(0, await db.ExamItems.CountAsync(b => b.ExamId == examId));
        }

        ExamItemBindingDto binding;
        await using (var db = Db())
        {
            binding = await new AdminExamService(db, TimeProvider.System)
                .BindItemAsync(examId, new BindPoolItemRequest(sectionId, created.ItemVersionId, DisplayOrder: 1));
        }

        Assert.Equal(examId, binding.ExamId);
        Assert.Equal(created.ItemId, binding.ItemId);
        Assert.Equal(created.ItemVersionId, binding.ItemVersionId);
        Assert.Equal(ModalityDto.Reading, binding.Modality);

        await using (var db = Db())
        {
            var stored = await db.ExamItems.AsNoTracking().SingleAsync(b => b.ExamId == examId);
            Assert.Equal(created.ItemVersionId, stored.ItemVersionId);
            Assert.Equal(1, stored.DisplayOrder);
        }
    }

    /// <summary>
    /// INV-6 from the authoring side: a production paper cannot bind the demo item authoring just
    /// produced. The refusal is the trigger's, surfaced as a sentence rather than a 500.
    /// </summary>
    [Fact]
    public async Task A_production_paper_cannot_bind_a_newly_authored_demo_item()
    {
        var topicId = await ReadingTopicAsync();
        var certCode = Unique("IPR");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Prod Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount, ContentTier)
            VALUES (@cert, N'Paid paper', 'draft', 60, 'mock', 1, 'production');
            """);

        int examId;
        int sectionId;
        await using (var db = Db())
        {
            var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
            examId = await db.Exams.Where(e => e.CertId == cert.CertId).Select(e => e.ExamId).SingleAsync();
            sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        }

        var created = await CreateService().CreateAsync(ReadingItem(topicId, "What time does the depot open?"));

        await using var db2 = Db();
        var refused = await Assert.ThrowsAsync<AdminExamException>(() =>
            new AdminExamService(db2, TimeProvider.System)
                .BindItemAsync(examId, new BindPoolItemRequest(sectionId, created.ItemVersionId, 1)));

        Assert.Contains("INV-6", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db2.ExamItems.CountAsync(b => b.ExamId == examId));
    }

    /// <summary>
    /// AC4: the existing exam editor still previews, and once an authored item is bound it previews
    /// <em>the authored version</em>. This drives <c>GetExamPreviewTakeAsync</c> — the production
    /// preview path, with no stub anywhere — on a paper whose position holds an item the author created
    /// through <c>CreateAsync</c> in the test body.
    /// <para>
    /// <b>#269: the legacy skeleton this fixture used to seed is gone with the table.</b> It carried its
    /// own key (B) and its own stem so the test could prove the authored version won the precedence
    /// contest. With one content source left there is no contest to referee, so what the test proves is
    /// the round trip that remains: the author's write binds to a position, and the preview renders that
    /// write's stem, key and explanation rather than anything the fixture invented. The skeleton was
    /// also load-bearing until #269 - a position with no question row was refused as a hole - and a
    /// binding row is now the whole of what a position needs.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_preview_renders_the_version_the_author_wrote()
    {
        var topicId = await ReadingTopicAsync();
        var certCode = Unique("IPV");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Preview Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60, 100, 450, 50);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'RDT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1), (@cert, 'HIGH', N'High', 500, 990, 2);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Authored preview paper', 'draft', 60, 'mock', 1);
            """);

        int examId;
        int sectionId;
        await using (var db = Db())
        {
            var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
            examId = await db.Exams.Where(e => e.CertId == cert.CertId).Select(e => e.ExamId).SingleAsync();
            sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        }

        // The author writes the item and its reason; the key is C.
        var created = await CreateService().CreateAsync(
            new CreatePoolItemRequest(
                ModalityDto.Reading, topicId, "B1", "sentence_cloze",
                "What time does the depot open?",
                "Six in the morning.", "Six in the evening.", "Eight in the morning.", "Eight in the evening.",
                "C",
                "The author's own explanation, which the preview must show.",
                RationaleA: "The passage gives the opening time, which is six in the morning.",
                RationaleB: "The passage gives the opening time, not the closing time.",
                RationaleC: "Correct: eight in the morning is what the passage states.",
                RationaleD: "The passage gives a single opening time."));

        await using (var db = Db())
        {
            await new AdminExamService(db, TimeProvider.System)
                .BindItemAsync(examId, new BindPoolItemRequest(sectionId, created.ItemVersionId, 1));
        }

        await using (var db = Db())
        {
            var preview = await new AdminExamService(db, TimeProvider.System)
                .GetExamPreviewTakeAsync(examId, questionDisplayOrder: null);

            Assert.Equal(1, preview.Take.TotalQuestions);
            var rendered = preview.Take.CurrentQuestion;
            Assert.Equal("What time does the depot open?", rendered.Stem);

            var answer = Assert.Single(preview.Answers);
            var correct = Assert.Single(answer.CorrectOptionIds);
            Assert.Equal("C", rendered.Options.Single(o => o.OptionId == correct).Label);
            Assert.Equal("The author's own explanation, which the preview must show.", answer.Explanation);
        }
    }
}
