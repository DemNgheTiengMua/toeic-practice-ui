using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
/// #203 / S-05: the studio writes a stimulus and its children together, through the one ingestion
/// seam.
/// <para>
/// Every assertion that matters is read back off the database. "The bundle was saved" is a statement
/// about <c>Stimuli</c>, <c>Items</c> and <c>ItemVersions</c>; a test that asserted the returned DTO
/// would pass a build that returned a bundle and wrote nothing, which is the shape of the defect this
/// ticket was filed against.
/// </para>
/// <para>
/// The fakes are <see cref="FakeVerifierModel"/> and <see cref="FakeLevelClassifier"/>, scripted so a
/// bundle passing this seam is one whose gates actually ran. A fake that always agreed would make
/// every save pass for the wrong reason.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StimulusStudioTests(DatabaseFixture database) : IAsyncLifetime
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// Starts and ends clean. The end matters as much as the start: gate one's key-balance rule is a
    /// statement about a <em>cell</em>, so a bundle left behind by this class changes the distribution
    /// every later class in the same (topic, level) cell is judged against. Measured: without the end
    /// cleanup, <c>PoolIngestionTests.Gate_one_counts_the_bundle_s_own_keys_against_the_cell_balance</c>
    /// failed only in the full assembly and passed alone.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await ClearAsync();

    private StimulusStudioService Service(
        CompanionDbContext db,
        bool withModels = true,
        PoolGateTwo.IGateTwoTranscriber? transcriber = null)
    {
        var gates = withModels
            ? new PoolIngestionGates(
                db,
                verifierOne: new FakeVerifierModel("verifier-a", "v1")
                {
                    Probabilities = Confident(), BlindProbabilities = Blind(),
                },
                verifierTwo: new FakeVerifierModel("verifier-b", "v2")
                {
                    Probabilities = Confident(), BlindProbabilities = Blind(),
                },
                ablationSolver: null,
                classifier: new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B1 },
                transcriber: transcriber)
            // No verifier, no classifier: the shape every unconfigured deployment starts in, and the
            // one AC5's "cannot be saved as validated" must hold in.
            : new PoolIngestionGates(db);

        // #256: a studio save now records the upload it is, so the service needs the acting staff
        // account. A fixed id is enough here: the fixture clears its own rows and the self-approval
        // trigger is exercised in IngestionWorkbenchTests, not in a studio test.
        return new StimulusStudioService(db, TimeProvider.System, gates, new FixedActing(StaffUserId(db, "author")));
    }

    /// <summary>A seeded staff account, created on first use. A studio save names its uploader, and a
    /// clearance must name a different one: ADM-02 refuses a reviewer who uploaded the batch.</summary>
    private static int StaffUserId(CompanionDbContext db, string tag)
    {
        var role = db.Roles.FirstOrDefault(r => r.Code == "studio-staff");
        if (role is null)
        {
            role = new Role { Code = "studio-staff", Name = "Studio staff" };
            db.Roles.Add(role);
            db.SaveChanges();
        }

        var email = $"studio-{tag}@test.local";
        var user = db.Users.FirstOrDefault(u => u.Email == email);
        if (user is null)
        {
            user = new User { RoleId = role.RoleId, Email = email, PasswordHash = [1], FullName = $"Studio {tag}" };
            db.Users.Add(user);
            db.SaveChanges();
        }

        return user.UserId;
    }

    private sealed class FixedActing(int userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    /// <summary>
    /// An honest independent transcription, so gate two's acoustic rule passes and only the rule under
    /// test can refuse an audio bundle. Without one every listening save is refused for a check that did
    /// not run, and the audio tests would prove nothing about the rules they name.
    /// </summary>
    private sealed class EchoTranscriber(Func<string> text) : PoolGateTwo.IGateTwoTranscriber
    {
        public System.Threading.Tasks.Task<string> TranscribeAsync(
            string audioPath, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(text());
    }

    private static Dictionary<string, decimal> Confident() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0.90m, ["B"] = 0.04m, ["C"] = 0.03m, ["D"] = 0.03m,
    };

    private static Dictionary<string, decimal> Blind() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0.28m, ["B"] = 0.24m, ["C"] = 0.24m, ["D"] = 0.24m,
    };

    /// <summary>
    /// The B1 passage <c>GateTwoTests</c> holds inside every D0.1 and D0.2 bound. Reusing it means a
    /// failure here is about the studio, not about a passage nobody calibrated.
    /// </summary>
    private static string Passage => PoolGateTwoTests.B1Passage;

    /// <summary>The listening script the audio fixtures share. Named so the cleanup can find its rows.</summary>
    private static string Script => PoolGateTwoTests.B1Script;

    private async Task SeedTopicsAsync()
    {
        await ClearAsync();
        await using var db = Db();
        await EnsureTopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");
        await EnsureTopicAsync(db, "reading", "R-INF", "Bridging inference and logical deduction");
        await EnsureTopicAsync(db, "listening", "L-DET", "Explicit factual detail and extraction");
    }

    /// <summary>
    /// A per-run token appended to every stem this fixture writes. It is what makes the cleanup able to
    /// remove <em>only</em> these rows: a filter on the passage text would also match the stimuli
    /// <c>PoolGateTwoTests</c> and <c>PoolIngestionTests</c> create from the same canonical passage, and
    /// deleting their items is a failure that arrives in an unrelated class (measured: it did, as
    /// <c>FK_PracticeAnswers_VersionItem</c>). The token is a nonce, so it appears in no option and
    /// cannot trip gate one's shared-lemma rule.
    /// </summary>
    private static readonly string Nonce = $"studio{Guid.NewGuid():N}"[..12];

    /// <summary>
    /// Removes only the rows this fixture wrote, anchored on <see cref="Nonce"/>.
    /// <para>
    /// #256: a save with verifier models now stages <c>validated</c>, and CNT-43 refuses the hard delete
    /// of anything that has left draft - so this fixture disables that trigger for its own rows rather
    /// than weakening it. The rows are this class's own, identified by the nonce, and a test catalog is
    /// the one place the delete may be stepped around (<c>PoolIngestionTests</c> sets the same
    /// precedent). The trigger itself is untouched and domain-level tests still prove it fires.
    /// </para>
    /// </summary>
    private async Task ClearAsync()
    {
        var like = $"%{Nonce}%";
        await database.SqlAsync($"""
            DECLARE @mine TABLE (ItemId INT);
            INSERT INTO @mine (ItemId)
              SELECT i.ItemId FROM Items i
              WHERE EXISTS (SELECT 1 FROM ItemVersions v WHERE v.ItemId = i.ItemId AND v.Stem LIKE N'{like}');

            -- #256: rows this fixture staged as `validated` are not deletable under CNT-43, and a review
            -- decision is not deletable at all under ADM-02 - both invariants working. Removing them is a
            -- fixture concern, so the guards are stepped around for exactly these rows and turned back on
            -- immediately. The versions go first because the trigger's own cascade used to remove them.
            ALTER TABLE Items DISABLE TRIGGER trg_Items_NoHardDelete;
            ALTER TABLE ReviewSampleDecisions DISABLE TRIGGER trg_ReviewSampleDecisions_NoSelfApproval;
            DELETE FROM ImportLog WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE FROM ReviewSampleDecisions WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE FROM ItemVersions WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE i FROM Items i WHERE i.ItemId IN (SELECT ItemId FROM @mine);
            ALTER TABLE ReviewSampleDecisions ENABLE TRIGGER trg_ReviewSampleDecisions_NoSelfApproval;
            ALTER TABLE Items ENABLE TRIGGER trg_Items_NoHardDelete;

            DELETE s FROM Stimuli s
              WHERE (s.Passage = N'{Escape(Passage)}' OR s.Transcript = N'{Escape(Script)}')
                AND NOT EXISTS (SELECT 1 FROM Items i WHERE i.StimulusId = s.StimulusId);
            """);
    }

    /// <summary>A single quote in a T-SQL literal, doubled. The fixtures are prose and carry apostrophes.</summary>
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task EnsureTopicAsync(CompanionDbContext db, string modality, string code, string name)
    {
        if (await db.Topics.AnyAsync(t => t.Modality == modality && t.Code == code))
        {
            return;
        }

        db.Topics.Add(new Topic { Modality = modality, Code = code, Name = name });
        await db.SaveChangesAsync();
    }

    private static StimulusDraftChild Child(
        int position,
        string topicCode,
        string stem,
        string level = "B1",
        string key = "A",
        int spanStart = 0,
        int spanEnd = 30) =>
        new(
            position,
            level,
            topicCode,
            $"{stem} [{Nonce}]",
            "The night ledger.",
            "The depot shutter.",
            "The depot supervisor.",
            "The office doorway.",
            key,
            "The first sentence states the opening time.",
            "Correct: the passage states the opening time.",
            "The passage never mentions a shutter.",
            "No supervisor appears in the passage.",
            "A door is not what the notice requires.",
            spanStart,
            spanEnd,
            "opens at six");

    /// <summary>
    /// A legal three-child reading bundle: three B1 children, non-overlapping spans, the passage's own
    /// text. This is the shape the screen produces when the author does nothing wrong.
    /// </summary>
    private static StimulusDraftRequest ReadingBundle() => new(
        ModalityDto.Reading,
        "B1",
        "passage_comprehension",
        [
            Child(1, "R-DET", "What time does the depot open?", spanStart: 0, spanEnd: 40),
            Child(2, "R-INF", "Why did the staff prepare all winter?", spanStart: 40, spanEnd: 90),
            Child(3, "R-INF", "Which room can be booked for two hours?", spanStart: 90, spanEnd: 150),
        ],
        Passage: Passage,
        DomainContext: "workplace");

    // -----------------------------------------------------------------------------------------
    // AC1: the picker locks the count for the level the author picked
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// AC1, read from the endpoint the screen calls. <b>Which guard could this pass while being wrong:</b>
    /// a quota keyed on the level alone gives A2 listening three slots instead of two, and the screen
    /// then pre-creates a child the volume table never packs.
    /// </summary>
    [Fact]
    public async Task The_options_report_the_locked_count_for_every_level_and_type()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var options = await Service(db).GetOptionsAsync("reading");

        Assert.Equal(6, options.Levels.Count);
        Assert.Equal("char", options.SpanUnit);
        // The closed twenty, not whatever this catalog holds: the two reading codes this fixture seeded
        // are present and a listening code is not.
        Assert.Contains(options.Topics, topic => topic.Code == "R-DET");
        Assert.Contains(options.Topics, topic => topic.Code == "R-INF");
        Assert.DoesNotContain(options.Topics, topic => topic.Code == "L-DET");

        var b2 = Assert.Single(options.Quotas, quota => quota.Level == "B2" && quota.ItemType == "passage_comprehension");
        Assert.Equal(4, b2.Locked);
        Assert.Equal(3, b2.MinimumAtLevel);
        Assert.Equal(["B1", "B2", "C1"], b2.AllowedChildLevels);

        // The A2 cloze is one child, and the A2 passage is three: the two differ inside one band.
        var a2Cloze = Assert.Single(options.Quotas, quota => quota.Level == "A2" && quota.ItemType == "sentence_cloze");
        var a2Passage = Assert.Single(options.Quotas, quota => quota.Level == "A2" && quota.ItemType == "passage_comprehension");
        Assert.Equal(1, a2Cloze.Locked);
        Assert.Equal(3, a2Passage.Locked);
    }

    /// <summary>
    /// The allowed levels are D0.3's, not the screen's: at A2 the only option is A2 itself, so a child
    /// selector built from this list cannot offer B1.
    /// </summary>
    [Fact]
    public async Task The_options_offer_no_level_outside_the_stimulus_envelope()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var options = await Service(db).GetOptionsAsync("listening");

        var a2 = Assert.Single(options.Quotas, quota => quota.Level == "A2" && quota.ItemType == "monologue_talk");
        Assert.Equal(["A2"], a2.AllowedChildLevels);

        var c1 = Assert.Single(options.Quotas, quota => quota.Level == "C1" && quota.ItemType == "monologue_talk");
        Assert.Equal(["B2", "C1", "C2"], c1.AllowedChildLevels);
    }

    // -----------------------------------------------------------------------------------------
    // AC1/AC5: the save writes the stimulus and every child, atomically
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// AC1 and AC2 on the database. A saved reading bundle is one <c>Stimuli</c> row, three
    /// <c>Items</c> rows pointing at it, and three <c>ItemVersions</c>. <b>Which guard could this pass
    /// while being wrong:</b> a save that wrote one stimulus per child, which is the passage duplicated
    /// per item that CNT-12 exists to stop.
    /// </summary>
    [Fact]
    public async Task A_saved_bundle_writes_one_stimulus_and_every_child_against_it()
    {
        await SeedTopicsAsync();
        await using var db = Db();
        var request = ReadingBundle();

        var before = await db.Stimuli.CountAsync();
        var saved = await Service(db).SaveAsync(request);

        await using var read = Db();
        Assert.Equal(before + 1, await read.Stimuli.CountAsync());

        var stimulus = await read.Stimuli.AsNoTracking().SingleAsync(s => s.StimulusId == saved.StimulusId);
        Assert.Equal("B1", stimulus.CefrLevel.Code);
        Assert.Equal(Passage, stimulus.Passage);
        Assert.Equal("workplace", stimulus.DomainContext);

        var items = await read.Items.AsNoTracking()
            .Where(item => item.StimulusId == saved.StimulusId)
            .OrderBy(item => item.ItemId)
            .ToListAsync();

        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal("reading", item.Modality);
            Assert.Equal("B1", item.CefrLevel.Code);
            Assert.Equal("passage_comprehension", item.ItemType);
            // #256: gate three RAN here (the service is built with models), so the item is staged
            // `validated` - the state that means "gates passed, awaiting audit". Before #256 the studio
            // wrote `draft`, which no promotion path could see.
            Assert.Equal("validated", item.LifecycleStatus);
            Assert.Equal("demo", item.ContentTier);
        });

        // Every child has exactly one version, and the version's span is what the studio bound - not a
        // value re-derived from the position.
        var versions = await read.ItemVersions.AsNoTracking()
            .Where(version => items.Select(item => item.ItemId).Contains(version.ItemId))
            .OrderBy(version => version.ItemId)
            .ToListAsync();

        Assert.Equal(3, versions.Count);
        Assert.All(versions, version => Assert.Equal(1, version.VersionNumber));
        Assert.Equal(0, versions[0].EvidenceStart);
        Assert.Equal(40, versions[0].EvidenceEnd);
        Assert.Equal("char", versions[0].EvidenceUnit);
        Assert.Equal(40, versions[1].EvidenceStart);
        Assert.Equal(90, versions[1].EvidenceEnd);
        Assert.Equal(150, versions[2].EvidenceEnd);
    }

    /// <summary>
    /// The write goes through <see cref="StaffAccountStampInterceptor"/>'s guarded tables and declares
    /// no tier or status the request could force. <b>Which guard could this pass while being wrong:</b>
    /// a request that carried <c>ContentTier</c> lets an author mint production stock straight past
    /// INV-6, which is why the studio's request shape has no such field.
    /// </summary>
    [Fact]
    public void The_draft_request_carries_no_tier_and_no_status()
    {
        var names = typeof(StimulusDraftRequest)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("ContentTier", names);
        Assert.DoesNotContain("LifecycleStatus", names);

        var childNames = typeof(StimulusDraftChild)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("ContentTier", childNames);
        Assert.DoesNotContain("LifecycleStatus", childNames);
    }

    // -----------------------------------------------------------------------------------------
    // AC5: a bundle that would fail a gate cannot be saved, and the reason names the gate
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input this guard must refuse.</b> Four children whose spans all cite the first sentence.
    /// Each child is individually legal - four options, a key, an explanation, a rationale - so gate one
    /// passes all four; only gate four's sibling rule sees them as one defect. The save must refuse and
    /// the refusal must name gate four, because "the bundle is invalid" tells the author which field but
    /// not which rule.
    /// </summary>
    [Fact]
    public async Task A_bundle_whose_children_share_a_span_is_refused_by_gate_four_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var itemsBefore = await db.Items.CountAsync();
        var stimuliBefore = await db.Stimuli.CountAsync();

        var request = ReadingBundle() with
        {
            Children =
            [
                Child(1, "R-DET", "What time does the depot open?", spanStart: 0, spanEnd: 40),
                Child(2, "R-INF", "Why did the staff prepare all winter?", spanStart: 0, spanEnd: 40),
                Child(3, "R-INF", "Which room can be booked for two hours?", spanStart: 0, spanEnd: 40),
            ],
        };

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));

        Assert.Equal(ApiError.Codes.Validation, refused.Code);
        Assert.Contains(GateFourRules.SpanOverlap, refused.Message, StringComparison.Ordinal);

        await using var read = Db();
        Assert.Equal(itemsBefore, await read.Items.CountAsync());
        Assert.Equal(stimuliBefore, await read.Stimuli.CountAsync());
    }

    /// <summary>
    /// The analysis the screen reads names the gate, so the author is told which rule to satisfy.
    /// <b>Which guard could this pass while being wrong:</b> an analysis that returned only the rule
    /// code and no gate leaves the screen to guess, and the guess will be the wrong panel.
    /// </summary>
    [Fact]
    public async Task The_analysis_names_gate_four_for_an_overlapping_bundle()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = ReadingBundle() with
        {
            Children =
            [
                Child(1, "R-DET", "What time does the depot open?", spanStart: 0, spanEnd: 40),
                Child(2, "R-INF", "Why did the staff prepare all winter?", spanStart: 0, spanEnd: 40),
                Child(3, "R-INF", "Which room can be booked for two hours?", spanStart: 90, spanEnd: 150),
            ],
        };

        var analysis = await Service(db).AnalyseAsync(request);

        // Three children on one span is three overlapping pairs, and each is refused by name - not one
        // refusal that hides the other two behind it.
        var overlaps = analysis.Refusals.Where(refusal => refusal.Rule == GateFourRules.SpanOverlap).ToList();
        Assert.Equal(3, overlaps.Count);
        Assert.All(overlaps, refusal => Assert.Equal("gate four", refusal.Gate));
        Assert.False(analysis.Acceptable);

        // And the indicator's own figure: identical spans are 1.00, which is over the ceiling.
        var pair = Assert.Single(
            analysis.Overlaps,
            entry => entry.FirstPosition == 1 && entry.SecondPosition == 2);
        Assert.Equal(1m, pair.Overlap);
        Assert.True(pair.OverCeiling);
        Assert.Equal(1m, analysis.MaximumOverlap);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a check that runs on the analysis but not
    /// on the write. This is the same bundle the analysis refuses, pushed through the save path: the
    /// row count must not move, which is the fact a view-model assertion cannot establish.
    /// </summary>
    [Fact]
    public async Task A_gate_one_refusal_on_the_save_path_writes_no_item_and_no_stimulus()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        // Child 2 carries no explanation and no rationale. Gate one refuses either alone; both are
        // removed so the refusal cannot be satisfied by one of them.
        var request = ReadingBundle() with
        {
            Children =
            [
                Child(1, "R-DET", "What time does the depot open?", spanStart: 0, spanEnd: 40),
                Child(2, "R-INF", "Why did the staff prepare all winter?", spanStart: 40, spanEnd: 90) with
                {
                    Explanation = null,
                    RationaleA = null, RationaleB = null, RationaleC = null, RationaleD = null,
                },
                Child(3, "R-INF", "Which room can be booked for two hours?", spanStart: 90, spanEnd: 150),
            ],
        };

        var itemsBefore = await db.Items.CountAsync();
        var stimuliBefore = await db.Stimuli.CountAsync();

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));

        Assert.Contains(GateOneRules.Explanation, refused.Message, StringComparison.Ordinal);

        await using var read = Db();
        Assert.Equal(itemsBefore, await read.Items.CountAsync());
        Assert.Equal(stimuliBefore, await read.Stimuli.CountAsync());
    }

    /// <summary>
    /// AC5's own sentence: a bundle that fails a shape rule cannot be saved, and the refusal names the
    /// rule. This is D0.3's envelope, which no gate states.
    /// </summary>
    [Fact]
    public async Task A_child_outside_the_stimulus_envelope_cannot_be_saved()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = ReadingBundle() with
        {
            StimulusLevel = "A2",
            Children =
            [
                Child(1, "R-DET", "What time does the depot open?", level: "A2", spanStart: 0, spanEnd: 40),
                Child(2, "R-INF", "Why did the staff prepare all winter?", level: "C1", spanStart: 40, spanEnd: 90),
            ],
        };

        var refused = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));

        Assert.Contains(StimulusStudioRules.ChildLevel, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> never naming the checks that did not run.
    /// The studio has no verifier models, so gate three cannot certify a key, and a save that said
    /// nothing would read as a certified bundle. The analysis must say so by name.
    /// </summary>
    [Fact]
    public async Task The_analysis_names_gate_three_as_not_run_and_the_save_still_writes_a_draft()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = ReadingBundle();
        var analysis = await Service(db, withModels: false).AnalyseAsync(request);

        // No verifier models in this test's service: gate three never runs, and saying so by name is the
        // only honest report. It does not block the save, because the save writes a draft.
        Assert.Contains(
            analysis.UnrunChecks,
            refusal => refusal.Gate == "gate three" && refusal.Message.Contains("not run", StringComparison.Ordinal));

        var saved = await Service(db, withModels: false).SaveAsync(request);

        await using var read = Db();
        var items = await read.Items.AsNoTracking()
            .Where(item => item.StimulusId == saved.StimulusId)
            .ToListAsync();

        Assert.Equal(3, items.Count);
        // #256: no verifier models, so gate three did not run and the item correctly stays `draft`.
        // Staging it `validated` here would claim a check that never happened.
        Assert.All(items, item => Assert.Equal("draft", item.LifecycleStatus));
    }

    // -----------------------------------------------------------------------------------------
    // AC3: the audio bundle - order, gap, and the unit
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The B1 listening script <c>PoolGateTwoTests</c> holds inside every D0.2 listening bound. Reusing
    /// it means an audio bundle's only possible refusal is the rule under test - a transcript nobody
    /// calibrated would fail the length or syntax bound and make every audio test prove the wrong thing.
    /// </summary>
    private static string ListeningTranscript => PoolGateTwoTests.B1Script;

    private async Task<StimulusDraftRequest> ListeningBundleAsync(string[] topics, int[][] spans)
    {
        await using var db = Db();
        var children = new List<StimulusDraftChild>();
        for (var index = 0; index < topics.Length; index++)
        {
            children.Add(Child(
                index + 1,
                topics[index],
                $"Which detail is stated at point {index + 1}?",
                spanStart: spans[index][0],
                spanEnd: spans[index][1]));
        }

        return new StimulusDraftRequest(
            ModalityDto.Listening,
            "B1",
            "monologue_talk",
            children,
            Transcript: ListeningTranscript,
            AudioPath: "audio/b1-monologue.mp3",
            DomainContext: "workplace");
    }

    /// <summary>
    /// The service an audio test needs: gate two's acoustic rule is satisfied by an honest
    /// transcription, so a refusal names the order or gap rule rather than "not checked".
    /// </summary>
    private StimulusStudioService AudioService(CompanionDbContext db) =>
        Service(db, transcriber: new EchoTranscriber(() => ListeningTranscript));

    /// <summary>
    /// AC3's gap badge. Three detail children whose evidence is 20 s apart are three gaps of 8.0 s or
    /// more, so nothing is refused and the badge reports the figure the rule uses. <b>Which guard could
    /// this pass while being wrong:</b> measuring start-to-start instead of end-to-start reports a gap
    /// of 30 s here, which is the wrong quantity.
    /// </summary>
    [Fact]
    public async Task An_audio_bundle_reports_the_end_to_start_gap_between_adjacent_detail_items()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        // Spans 0-10, 30-40, 60-70: each gap is 30 - 10 = 20 s, well over the 8 s floor.
        var request = await ListeningBundleAsync(
            ["L-DET", "L-DET", "L-DET"],
            [[0, 10], [30, 40], [60, 70]]);

        var analysis = await AudioService(db).AnalyseAsync(request);

        Assert.Equal(2, analysis.Gaps.Count);
        Assert.All(analysis.Gaps, gap =>
        {
            Assert.Equal(20m, gap.Seconds);
            Assert.False(gap.BelowFloor);
        });

        Assert.DoesNotContain(
            analysis.Refusals,
            refusal => refusal.Rule == GateFourRules.AudioGap && refusal.Gate == "gate four");
    }

    /// <summary>
    /// <b>The input this guard must refuse.</b> Two adjacent detail items 5 s apart. The learner has
    /// five seconds between the end of one cue and the start of the next, which CNT-10 calls too short.
    /// </summary>
    [Fact]
    public async Task An_audio_bundle_below_the_eight_second_floor_is_refused_by_gate_four()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = await ListeningBundleAsync(
            ["L-DET", "L-DET", "L-DET"],
            [[0, 10], [15, 25], [40, 50]]);

        var analysis = await AudioService(db).AnalyseAsync(request);

        Assert.Contains(analysis.Refusals, refusal => refusal.Rule == GateFourRules.AudioGap);
        var gap = Assert.Single(analysis.Gaps, entry => entry.Seconds == 5m);
        Assert.True(gap.BelowFloor);

        await Assert.ThrowsAsync<AdminItemException>(() => AudioService(db).SaveAsync(request));
    }

    /// <summary>
    /// <b>The input this guard must refuse.</b> Children served out of the order their evidence is
    /// heard: child 2 cites 60-70 s while child 1 cites 0-10 s, but the authored order puts the later
    /// evidence first. Audio plays linearly, so an item whose evidence has passed cannot be answered.
    /// </summary>
    [Fact]
    public async Task An_audio_bundle_whose_evidence_is_out_of_order_is_refused_by_gate_four()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = await ListeningBundleAsync(
            ["L-DET", "L-DET", "L-DET"],
            [[60, 70], [0, 10], [100, 110]]);

        var analysis = await AudioService(db).AnalyseAsync(request);

        Assert.Contains(analysis.Refusals, refusal => refusal.Rule == GateFourRules.AudioOrder);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> accepting a child-level unit the caller
    /// supplies. A listening child whose span says "char" is read as seconds by gate four's gap rule,
    /// which then reports a gap of hundreds of seconds for a bundle nobody timed (CNT-11). The unit is
    /// therefore derived from the skill and is not a field the request can carry, and the shape rule
    /// refuses one that disagrees.
    /// </summary>
    [Fact]
    public void The_unit_is_the_skills_and_is_not_a_child_field()
    {
        var names = typeof(StimulusDraftChild)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("EvidenceUnit", names);
        Assert.Equal("sec", StimulusEnvelope.SpanUnitFor("listening"));
        Assert.Equal("char", StimulusEnvelope.SpanUnitFor("reading"));

        var charBundle = StimulusEnvelope.Review(
            "listening", CefrLevel.B1, "monologue_talk",
            [CefrLevel.B1, CefrLevel.B1, CefrLevel.B1], "char");

        Assert.Contains(charBundle, refusal => refusal.Rule == StimulusStudioRules.SpanUnit);
    }

    /// <summary>
    /// The saved listening bundle stores seconds, not characters, and every child points at the one
    /// stimulus. <b>Which guard could this pass while being wrong:</b> writing <c>char</c> for a
    /// listening version leaves the evidence span unrenderable on the review screen, where a jump to
    /// audio expects a timestamp.
    /// </summary>
    [Fact]
    public async Task A_saved_audio_bundle_stores_its_spans_in_seconds()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = await ListeningBundleAsync(
            ["L-DET", "L-DET", "L-DET"],
            [[0, 10], [30, 40], [60, 70]]);

        var saved = await AudioService(db).SaveAsync(request);

        await using var read = Db();
        var versions = await read.ItemVersions.AsNoTracking()
            .Where(version => read.Items
                .Where(item => item.StimulusId == saved.StimulusId)
                .Select(item => item.ItemId)
                .Contains(version.ItemId))
            .ToListAsync();

        Assert.Equal(3, versions.Count);
        Assert.All(versions, version => Assert.Equal("sec", version.EvidenceUnit));
        Assert.All(versions, version => Assert.True(version.EvidenceEnd > version.EvidenceStart));
    }

    // -----------------------------------------------------------------------------------------
    // #256: the save is an upload, so the audit and the clearance can see what it wrote
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// #256. A studio save records a batch and one <c>ImportLog</c> row per item it wrote, and a
    /// <em>different</em> account can then clear that item for service.
    /// <para>
    /// <b>The defect this covers, measured.</b> The studio wrote items with no batch and no log row.
    /// <c>StratifiedAuditService</c> draws its strata from <c>ImportLog</c> and
    /// <see cref="IngestionWorkbenchService.ClearForServiceAsync"/> refuses an item that "did not arrive
    /// in batch {id}", so a studio item could never be promoted and never be served. The studio was a
    /// screen that could create content and never publish it.
    /// </para>
    /// <para>
    /// <b>Which guard could this pass while being wrong:</b> a save that writes the batch but no log
    /// row — the batch assertion alone would pass, and the clearance would still refuse. Every written
    /// item is therefore asserted present in the batch's log, and the clearance is driven for real.
    /// The step after this one — an <c>active</c> item being drawn by a drill — is
    /// <c>ItemPromotionTests.An_imported_item_reaches_a_pool_drill_only_after_it_is_promoted</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_saved_bundle_records_the_batch_the_audit_and_the_clearance_read()
    {
        await SeedTopicsAsync();
        await using var db = Db();
        var author = StaffUserId(db, "author");
        var reviewer = StaffUserId(db, "reviewer");

        var batchesBefore = await db.ImportBatches.CountAsync();
        var saved = await Service(db).SaveAsync(ReadingBundle());
        Assert.Equal(3, saved.Children.Count);

        await using var read = Db();
        Assert.Equal(batchesBefore + 1, await read.ImportBatches.CountAsync());

        var batch = await read.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.BatchId)
            .FirstAsync();

        // The upload names the acting account, which is what ADM-02's self-approval rule reads.
        Assert.Equal(author, batch.UploadedBy);
        Assert.Null(batch.CertId);

        var logged = await read.ImportLogs.AsNoTracking()
            .Where(l => l.BatchId == batch.BatchId && l.ItemId != null)
            .Select(l => l.ItemId!.Value)
            .ToListAsync();

        var written = saved.Children.Select(item => item.ItemId).ToList();
        Assert.All(written, id => Assert.Contains(id, logged));

        // A different account clears one item: validated -> active. The refusal when the uploader tries
        // is the database's own (ADM-02) and is covered in IngestionWorkbenchTests.
        await new IngestionWorkbenchService(db, TimeProvider.System, new PoolIngestionService(db, TimeProvider.System, Gates(db)))
            .ClearForServiceAsync(batch.BatchId, written[0], reviewer);

        await using var verify = Db();
        var cleared = await verify.Items.AsNoTracking().SingleAsync(i => i.ItemId == written[0]);
        Assert.Equal("active", cleared.LifecycleStatus);

        // A clearance is one item, not a batch.
        var uncleared = await verify.Items.AsNoTracking().SingleAsync(i => i.ItemId == written[1]);
        Assert.Equal("validated", uncleared.LifecycleStatus);
    }

    /// <summary>The same gates the save used, so the clearance runs against the same deployment shape.</summary>
    private PoolIngestionGates Gates(CompanionDbContext db) => new(
        db,
        verifierOne: new FakeVerifierModel("verifier-a", "v1") { Probabilities = Confident(), BlindProbabilities = Blind() },
        verifierTwo: new FakeVerifierModel("verifier-b", "v2") { Probabilities = Confident(), BlindProbabilities = Blind() },
        ablationSolver: null,
        classifier: new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B1 });
}
