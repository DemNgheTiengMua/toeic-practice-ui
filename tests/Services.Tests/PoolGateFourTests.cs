using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #197: gate four against the real pool.
/// <para>
/// The span and audio rules are pure functions and are tested exhaustively in
/// <see cref="GateFourTests"/> without a database. What only the database can show is <em>which items
/// are siblings</em> and <em>which version's span counts</em>: a bundle is a set of rows sharing a
/// <c>StimulusId</c>, and a re-versioned item cites different evidence. Both are facts the caller
/// cannot be trusted to hand in, and getting either wrong makes the rule judge a bundle that does not
/// exist.
/// </para>
/// <para>
/// Every test runs inside a transaction it rolls back: the pool is shared by the whole assembly.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PoolGateFourTests(DatabaseFixture database)
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

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

    /// <summary>One child of a shared stimulus, with the evidence span its review screen will highlight.</summary>
    private static Item Child(
        string modality,
        string itemType,
        int topicId,
        Stimulus stimulus,
        int? start,
        int? end,
        string? unit,
        string stem)
    {
        var item = new Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = CefrLevel.B1,
            ItemType = itemType,
            ContentTier = "demo",
            LifecycleStatus = "draft",
            Stimulus = stimulus,
        };

        item.ItemVersions.Add(new ItemVersion
        {
            VersionNumber = 1,
            Stem = stem,
            OptionA = "One.",
            OptionB = "Two.",
            OptionC = "Three.",
            OptionD = "Four.",
            KeyLabel = "A",
            RationaleA = "Because the passage says so.",
            RationaleB = "The passage does not say this.",
            RationaleC = "The passage does not say this.",
            RationaleD = "The passage does not say this.",
            Explanation = "The passage says so.",
            EvidenceStart = start,
            EvidenceEnd = end,
            EvidenceUnit = unit,
            EvidenceQuote = start is null ? null : "quoted evidence",
        });

        return item;
    }

    /// <summary>
    /// The rule's subject is the bundle, so a refusal must name the two stored items that overlap.
    /// <b>Which guard could this pass while being wrong:</b> a reader that loaded only the item under
    /// judgement finds no sibling and accepts every bundle, which looks exactly like a passing gate.
    /// </summary>
    [Fact]
    public async Task Two_stored_siblings_that_cite_the_same_span_are_refused()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        var inference = await TopicAsync(db, "reading", "R-INF", "Bridging inference across sentences");
        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            Passage = new string('x', 800),
            DomainContext = "general",
        };

        var first = Child("reading", "passage_comprehension", topic, stimulus, 100, 140, "char", "What did she do?");
        var second = Child("reading", "passage_comprehension", inference, stimulus, 110, 150, "char", "Why did she do it?");
        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var refusals = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.SpanOverlap, refusal.Rule);
        Assert.Contains(first.ItemId.ToString(), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(second.ItemId.ToString(), refusal.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The span that counts is the <em>latest version's</em>. <b>Which guard could this pass while
    /// being wrong:</b> reading version 1 of every item judges evidence that is no longer served - the
    /// item was re-versioned precisely because its span was wrong. Here version 1 overlaps its sibling
    /// and version 2 does not, so a reader taking either the first or the last row it happens to meet
    /// gives a different verdict.
    /// </summary>
    [Fact]
    public async Task The_latest_version_carries_the_span_the_rule_reads()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            Passage = new string('x', 800),
            DomainContext = "general",
        };

        var sibling = Child("reading", "passage_comprehension", topic, stimulus, 100, 140, "char", "What did she do?");
        var revised = Child("reading", "passage_comprehension", topic, stimulus, 110, 150, "char", "Where did she go?");
        db.Items.AddRange(sibling, revised);
        await db.SaveChangesAsync();

        // Before the revision the two cite the same characters; after it they do not.
        var before = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);
        Assert.Contains(before, r => r.Rule == GateFourRules.SpanOverlap);

        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = revised.ItemId,
            VersionNumber = 2,
            Stem = "Where did she go?",
            OptionA = "One.",
            OptionB = "Two.",
            OptionC = "Three.",
            OptionD = "Four.",
            KeyLabel = "A",
            RationaleA = "Because the passage says so.",
            RationaleB = "The passage does not say this.",
            RationaleC = "The passage does not say this.",
            RationaleD = "The passage does not say this.",
            Explanation = "The passage says so.",
            EvidenceStart = 600,
            EvidenceEnd = 640,
            EvidenceUnit = "char",
            EvidenceQuote = "quoted evidence",
        });
        await db.SaveChangesAsync();

        var after = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);
        Assert.Empty(after);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A child with no span is not a zero-length one. <b>Which guard could this pass while being
    /// wrong:</b> coalescing a missing span to 0-0 makes it overlap nothing, so a bundle where nobody
    /// recorded evidence reports as independent - and CNT-11's requirement disappears silently.
    /// </summary>
    [Fact]
    public async Task A_child_with_no_evidence_span_is_refused_rather_than_treated_as_empty()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var topic = await TopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            Passage = new string('x', 800),
            DomainContext = "general",
        };

        var spanned = Child("reading", "passage_comprehension", topic, stimulus, 100, 140, "char", "What did she do?");
        var unspanned = Child("reading", "passage_comprehension", topic, stimulus, null, null, null, "Why?");
        db.Items.AddRange(spanned, unspanned);
        await db.SaveChangesAsync();

        var refusals = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.SpanShape, refusal.Rule);
        Assert.Contains(unspanned.ItemId.ToString(), refusal.Message, StringComparison.Ordinal);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A listening bundle is judged by CNT-10 - order and the 8.0 s gap - and not by CNT-12's overlap
    /// rule, whose exception is a *whole-passage* item and whose unit is characters.
    /// <b>Which guard could this pass while being wrong:</b> running the span rule over second-based
    /// spans compares a fraction of two spans that have no passage length, so its gist exception can
    /// never be settled and every legal gist bundle is refused.
    /// </summary>
    [Fact]
    public async Task A_listening_bundle_is_judged_on_its_order_and_gap_not_on_character_overlap()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var detail = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");
        var gist = await TopicAsync(db, "listening", "L-GIST", "Global gist, purpose, macro-topic");
        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            AudioPath = "/audio/gate4/b1-dialogue.mp3",
            Transcript = new string('w', 400),
            DomainContext = "general",
        };

        // A gist item first, spanning the whole recording, then a detail item 8 s after it ends.
        var gistItem = Child("listening", "extended_dialogue", gist, stimulus, 0, 120, "sec", "What is the talk about?");
        var detailItem = Child("listening", "extended_dialogue", detail, stimulus, 128, 140, "sec", "When does it open?");
        db.Items.AddRange(gistItem, detailItem);
        await db.SaveChangesAsync();

        var refusals = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);

        Assert.Empty(refusals);

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The gap applies to the stored rows, so a bundle whose second detail item begins five seconds
    /// after the first ends is refused by the reader and not only by the studio.
    /// <b>Which guard could this pass while being wrong:</b> a reader that loaded one child's span for
    /// every child finds a gap of zero between identical spans - or, if it clamped, a legal one.
    /// </summary>
    [Fact]
    public async Task Stored_audio_children_closer_than_eight_seconds_are_refused()
    {
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var detail = await TopicAsync(db, "listening", "L-DET", "Detail and specific information in a dialogue");
        var stimulus = new Stimulus
        {
            CefrLevel = CefrLevel.B1,
            AudioPath = "/audio/gate4/b1-two-details.mp3",
            Transcript = new string('w', 400),
            DomainContext = "general",
        };

        var first = Child("listening", "extended_dialogue", detail, stimulus, 0, 30, "sec", "What time does it open?");
        var second = Child("listening", "extended_dialogue", detail, stimulus, 35, 45, "sec", "Who runs it?");
        db.Items.AddRange(first, second);
        await db.SaveChangesAsync();

        var refusals = await PoolGateFour.ReviewBundleAsync(db, stimulus.StimulusId);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.AudioGap, refusal.Rule);

        await transaction.RollbackAsync();
    }
}
