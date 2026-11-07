using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #195: gate two against the real pool.
/// <para>
/// The rules are a pure function and are tested exhaustively in <see cref="GateTwoTests"/> without a
/// database. What only a database can show is what the gate has to <em>find</em> before it can
/// measure: the stimulus a reading item hangs on, the transcript a listening item is checked
/// against, and the modality that decides which of D0.2's two columns applies. Every one of those is
/// a row the item does not carry, and getting any of them wrong is invisible from the item alone.
/// </para>
/// <para>
/// Every test runs inside a transaction it rolls back, because the pool is shared by the whole
/// assembly and a seeded stimulus would otherwise make later tests judge a table they did not build.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolGateTwoTests(DatabaseFixture database)
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// A B1 reading passage that satisfies every bound in D0.1 and D0.2: 299 words (reading is
    /// 220-300), MLTU 13.00 (10.5-13.5), 1.35 clauses per T-unit (1.3-1.6), FK 6.97 (6.0-8.0) and
    /// 95.3% coverage of the first 3,000 headwords (floor 90%). The figures are asserted by
    /// <c>GateTwoTests.The_B1_reading_fixture_sits_inside_every_bound</c>, so a rule change fails
    /// there rather than silently leaving this passage demonstrating nothing.
    /// </summary>
    public const string B1Passage =
        "The city library will move to a new building next month, and the staff have spent the winter "
        + "preparing for the change. The old building on Mill Street opened in 1972 and it has been too "
        + "small for many years. Because the number of members has grown quickly, the main reading room is "
        + "often full by ten in the morning. The new building on the river side has three floors, a large "
        + "room for children and a small café near the door. It also has a quiet room where students can "
        + "study for their exams, and the room can be booked for two hours. The library bought the land in "
        + "2019, but the work stopped for a year because the cost of building rose sharply. The city council "
        + "found the extra money in 2021 and the builders returned to the site in the spring. Most of the "
        + "books are already in boxes, so the old building will close two weeks before the move. Readers who "
        + "need a book during that time can ask the staff, and a van will bring it to the town hall. The "
        + "library will also keep its online service open during the move, so members can still borrow "
        + "digital books. The staff are proud of the new building, which has larger windows and much better "
        + "heating than the old one. They hope that more people will come in the evening, when the library "
        + "will stay open until nine. A small café will be run by a local family, because the money it makes "
        + "will go back into the fund. Children who join the library this summer will get a free book, which "
        + "they may keep. The van is ready because the shelves are full. The reading room is quiet because "
        + "the exam period has ended.";

    /// <summary>
    /// A sharing topic from the canonical twenty. Created only if the fixture database has none,
    /// following <c>PoolGateOneTests</c>: a synthetic code would be refused by the closed-vocabulary
    /// trigger, and the canonical row is what a real item sits on.
    /// </summary>
    private static async Task<int> TopicAsync(CompanionDbContext db, string modality, string code, string name)
    {
        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Modality == modality && t.Code == code);
        if (topic is not null)
        {
            return topic.TopicId;
        }

        topic = new Topic { Modality = modality, Code = code, Name = name };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.TopicId;
    }

    /// <summary>
    /// A listening item whose script passes gate two and whose audio an honest transcript matches.
    /// Nothing but the transcription may be able to refuse it.
    /// </summary>
    public const string B1Script =
        "Good morning, and thank you for calling the city library. "
        + "The library opens at nine and it closes at six on most days. "
        + "Because the main room is being painted, the reading area is upstairs this week. "
        + "You can return your books at the front desk, which is near the main door. "
        + "If you have an old card, please bring it with you when you come. "
        + "The new card costs nothing for students, because the city pays for them. "
        + "When you join, you may borrow four books at one time. "
        + "The library keeps the books for three weeks, so please bring them back on time. "
        + "You can call us again if you need more help. "
        + "Our staff will show you where the history books are. "
        + "The computer room is on the second floor, and it is quiet there. "
        + "It is free to use the computers for one hour each day. "
        + "We look forward to seeing you at the library soon.";

    /// <summary>A transcriber that returns whatever the test tells it to, standing in for Whisper.</summary>
    private sealed class FakeTranscriber(string text, Exception? failure = null) : PoolGateTwo.IGateTwoTranscriber
    {
        public Task<string> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default) =>
            failure is not null ? Task.FromException<string>(failure) : Task.FromResult(text);
    }

    private static async Task<Item> SeedItem(
        CompanionDbContext db,
        string modality,
        string itemType,
        CefrLevel level,
        int topicId,
        Stimulus stimulus,
        string stem = "What must every visitor do at the gate?")
    {
        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = level,
            ItemType = itemType,
            ContentTier = "demo",
            LifecycleStatus = "draft",
            Stimulus = stimulus,
        };

        item.ItemVersions.Add(new ItemVersion
        {
            VersionNumber = 1,
            Stem = stem,
            OptionA = "The night ledger.",
            OptionB = "The depot shutter.",
            OptionC = "The depot supervisor.",
            OptionD = "The office door.",
            KeyLabel = "A",
            RationaleA = "Correct: the notice names the ledger.",
            RationaleB = "The passage never mentions a shutter.",
            RationaleC = "No supervisor appears in the passage.",
            RationaleD = "A door is not what the notice requires.",
            Explanation = "The notice says every visitor signs the night ledger.",
        });

        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    /// <summary>
    /// The trap only a database can show: the item's <em>stem</em> is a 12-word question and its
    /// passage is the 299-word stimulus. A gate that measured the stem would refuse every reading
    /// item in the pool for being short; a gate that measured the passage accepts it. This asserts
    /// the measurements came from the passage, so switching the two sources fails here.
    /// </summary>
    [Fact]
    public async Task A_reading_item_is_profiled_on_its_passage_and_not_on_its_stem()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        var item = await SeedItem(db, "reading", "passage_comprehension", CefrLevel.B1, topic,
            new Stimulus { CefrLevel = CefrLevel.B1, Passage = B1Passage, DomainContext = "general" });

        var verdict = await PoolGateTwo.ReviewAsync(db, item.ItemId);

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));

        var measured = GateTwo.Measure(B1Passage, CefrLevel.B1);
        Assert.Equal("299", verdict.Measurements.Single(m => m.Name == "words").Value);
        Assert.Equal("13", verdict.Measurements.Single(m => m.Name == "mltu").Value);
        Assert.Equal("1.35", verdict.Measurements.Single(m => m.Name == "clauses_per_t_unit").Value);
        Assert.Equal(measured.Words.ToString(), verdict.Measurements.Single(m => m.Name == "words").Value);
        Assert.True(measured.Words > 200, "the fixture stopped being a B1-length passage");

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The review screen's transcript is what a learner reads, so a listening item whose recording
    /// does not match it is refused. This is the database half of D17: the transcript comes from the
    /// stimulus row, not from the item.
    /// </summary>
    [Fact]
    public async Task A_listening_item_whose_recording_disagrees_with_its_transcript_is_refused()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");
        var item = await SeedItem(db, "listening", "short_exchange", CefrLevel.B1, topic,
            new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                AudioPath = "/audio/gate2/b1-library.mp3",
                Transcript = B1Script,
                DomainContext = "general",
            });

        var refused = await PoolGateTwo.ReviewAsync(
            db, item.ItemId, new FakeTranscriber(B1Script.Replace("city library", "city bakery")));

        var refusal = Assert.Single(refused.Refusals, r => r.Rule == GateTwoRules.WordErrorRate);
        Assert.Contains("bakery", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // ... and the honest transcript is accepted, so the refusal above is about the difference
        var accepted = await PoolGateTwo.ReviewAsync(db, item.ItemId, new FakeTranscriber(B1Script));
        Assert.True(accepted.Accepted, string.Join(", ", accepted.Refusals.Select(r => r.Rule)));

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The seam the ticket warns about. No transcriber is configured, so 0.0% was never verified -
    /// and the gate says so rather than passing the item. A fake that quietly returns the transcript
    /// would make this rule unfalsifiable in production.
    /// </summary>
    [Fact]
    public async Task A_listening_item_with_no_transcriber_configured_is_refused_as_unverified()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");
        var item = await SeedItem(db, "listening", "short_exchange", CefrLevel.B1, topic,
            new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                AudioPath = "/audio/gate2/b1-library.mp3",
                Transcript = B1Script,
                DomainContext = "general",
            });

        var verdict = await PoolGateTwo.ReviewAsync(db, item.ItemId, transcriber: null);

        Assert.NotNull(verdict.Refusals.SingleOrDefault(r => r.Rule == GateTwoRules.TranscriptUnavailable));
        Assert.False(verdict.Accepted);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A transcriber that throws is a verifier that did not run. The item is refused as unverified,
    /// not crashed on and not passed: an ASR outage must not become a publishing gate.
    /// </summary>
    [Fact]
    public async Task A_failing_transcriber_is_unverified_and_not_a_crash()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");
        var item = await SeedItem(db, "listening", "short_exchange", CefrLevel.B1, topic,
            new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                AudioPath = "/audio/gate2/b1-library.mp3",
                Transcript = B1Script,
                DomainContext = "general",
            });

        var verdict = await PoolGateTwo.ReviewAsync(
            db, item.ItemId, new FakeTranscriber(string.Empty, new HttpRequestException("503 Service Unavailable")));

        var refusal = Assert.Single(verdict.Refusals, r => r.Rule == GateTwoRules.TranscriptUnavailable);
        Assert.Contains("503", refusal.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// D0.2's two columns, decided from the <c>Modality</c> column. The 160-word script is legal as
    /// listening (110-185) and too short as reading (220-300), so a gate that applied one envelope to
    /// both would be wrong on one of them. This asserts the mapping rather than the arithmetic.
    /// </summary>
    [Fact]
    public async Task The_modality_column_decides_which_D02_envelope_applies()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var readingTopic = await TopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        var listeningTopic = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");

        // the same B1 text, hung on a reading item and on a listening item
        var reading = await SeedItem(db, "reading", "passage_comprehension", CefrLevel.B1, readingTopic,
            new Stimulus { CefrLevel = CefrLevel.B1, Passage = B1Script, DomainContext = "general" });

        var listening = await SeedItem(db, "listening", "short_exchange", CefrLevel.B1, listeningTopic,
            new Stimulus
            {
                CefrLevel = CefrLevel.B1,
                AudioPath = "/audio/gate2/b1-library.mp3",
                Transcript = B1Script,
                DomainContext = "general",
            });

        Assert.NotNull(await RefusalAsync(db, reading.ItemId, GateTwoRules.WordCount));
        Assert.Null(await RefusalAsync(db, listening.ItemId, GateTwoRules.WordCount, new FakeTranscriber(B1Script)));

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A standalone <c>sentence_cloze</c> is bounded by D0.4 (15-45 words), not by D0.2. Its 20-word
    /// row is legal and a D0.2 reading envelope would refuse it for being under the 60-word A1 floor.
    /// The item type is a column, so the distinction is the database's to supply.
    /// </summary>
    [Fact]
    public async Task A_sentence_cloze_is_bounded_by_D04_and_not_by_the_stimulus_envelope()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "reading", "R-GRAM", "Grammatical form in a single sentence");
        var item = await SeedItem(db, "reading", "sentence_cloze", CefrLevel.A1, topic,
            new Stimulus
            {
                CefrLevel = CefrLevel.A1,
                Passage = "The cat is on the bed and the dog is in the box and the bird is in the ____ tree.",
                DomainContext = "general",
            });

        var verdict = await PoolGateTwo.ReviewAsync(db, item.ItemId);

        Assert.Null(verdict.Refusals.SingleOrDefault(r => r.Rule == GateTwoRules.WordCount));
        Assert.Equal("20", verdict.Measurements.Single(m => m.Name == "words").Value);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A speaking or writing prompt is not profiled by D0.2: its column is a learner-output demand.
    /// The gate says so as a notice rather than measuring a 15-word prompt against a 500-word
    /// envelope, which would refuse every productive task in the pool.
    /// </summary>
    [Fact]
    public async Task A_productive_item_is_reported_as_not_profiled()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "writing", "W-ARG", "Thesis-driven persuasive and opinion argumentation");
        var item = await SeedItem(db, "writing", "persuasive_essay", CefrLevel.B2, topic,
            new Stimulus
            {
                CefrLevel = CefrLevel.B2,
                PromptCopy = "Argue for or against a congestion charge. Give reasons and an example.",
                DomainContext = "general",
            });

        var verdict = await PoolGateTwo.ReviewAsync(db, item.ItemId);

        Assert.True(verdict.Accepted, string.Join(", ", verdict.Refusals.Select(r => r.Rule)));
        Assert.Contains(verdict.Notices, n => n.Rule == GateTwoRules.NotProfiled);

        await transaction.RollbackAsync();
    }

    private static async Task<GateTwoRefusal?> RefusalAsync(
        CompanionDbContext db, int itemId, string rule, PoolGateTwo.IGateTwoTranscriber? transcriber = null)
    {
        var verdict = await PoolGateTwo.ReviewAsync(db, itemId, transcriber);
        return verdict.Refusals.SingleOrDefault(r => r.Rule == rule);
    }
}
