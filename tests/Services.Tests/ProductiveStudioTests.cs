using System;
using System.Collections.Generic;
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
/// #204 / S-07: the productive studio writes a prompt and its parts through the one ingestion seam.
/// <para>
/// Every assertion that matters is read back off the database. "The task was saved" is a statement
/// about <c>Stimuli</c>, <c>Items</c> and <c>ItemVersions</c>; a test that asserted the returned DTO
/// would pass a build that returned a task and wrote nothing - and, worse, a build whose word range
/// and windows never reached the row the sitting renders. That is the defect this ticket's own traps
/// name first, so the ranges and the windows are asserted on <c>ItemVersions</c> in every case below.
/// </para>
/// <para>
/// The fakes are <see cref="FakeVerifierModel"/> and <see cref="FakeLevelClassifier"/>, scripted so a
/// task passing this seam is one whose gates actually ran. A fake that always agreed would make every
/// save pass for the wrong reason.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductiveStudioTests(DatabaseFixture database) : IAsyncLifetime
{
    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// A per-run token appended to every prompt this fixture writes. It is what makes the cleanup able
    /// to remove <em>only</em> these rows: a filter on the prompt text would also match a stimulus
    /// another class created from the same copy, and deleting their items is a failure that arrives in
    /// an unrelated class.
    /// </summary>
    private static readonly string Nonce = $"prod{Guid.NewGuid():N}"[..12];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await ClearAsync();

    /// <summary>
    /// Removes only the rows this fixture wrote, anchored on <see cref="Nonce"/>.
    /// <para>
    /// #256: a save with verifier models now stages <c>validated</c>, and CNT-43 refuses the hard delete
    /// of anything that has left draft - so this fixture disables that trigger for its own rows rather
    /// than weakening it. The rows are this class's own, identified by the nonce, and a test catalog is
    /// the one place the delete may be stepped around. The trigger itself is untouched and
    /// <c>ItemLifecycleTests</c> still proves it fires.
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

            ALTER TABLE Items DISABLE TRIGGER trg_Items_NoHardDelete;
            DELETE FROM ImportLog WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE FROM ReviewSampleDecisions WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE FROM ItemVersions WHERE ItemId IN (SELECT ItemId FROM @mine);
            DELETE i FROM Items i WHERE i.ItemId IN (SELECT ItemId FROM @mine);
            ALTER TABLE Items ENABLE TRIGGER trg_Items_NoHardDelete;

            DELETE s FROM Stimuli s
              WHERE s.PromptCopy LIKE N'{like}'
                AND NOT EXISTS (SELECT 1 FROM Items i WHERE i.StimulusId = s.StimulusId);
            """);
    }

    private ProductiveStudioService Service(
        CompanionDbContext db,
        bool withModels = true)
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
                classifier: new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B1 })
            // No classifier: the shape every unconfigured deployment starts in.
            : new PoolIngestionGates(db);

        return new ProductiveStudioService(db, TimeProvider.System, gates, new FixedActing(StaffUserId(db)));
    }

    /// <summary>The seeded author account, created on first use. A studio save names its uploader.</summary>
    private static int StaffUserId(CompanionDbContext db)
    {
        var role = db.Roles.FirstOrDefault(r => r.Code == "studio-staff");
        if (role is null)
        {
            role = new Role { Code = "studio-staff", Name = "Studio staff" };
            db.Roles.Add(role);
            db.SaveChanges();
        }

        const string email = "productive-studio-author@test.local";
        var user = db.Users.FirstOrDefault(u => u.Email == email);
        if (user is null)
        {
            user = new User { RoleId = role.RoleId, Email = email, PasswordHash = [1], FullName = "Studio author" };
            db.Users.Add(user);
            db.SaveChanges();
        }

        return user.UserId;
    }

    private sealed class FixedActing(int userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    private static Dictionary<string, decimal> Confident() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0.90m, ["B"] = 0.04m, ["C"] = 0.03m, ["D"] = 0.03m,
    };

    private static Dictionary<string, decimal> Blind() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0.28m, ["B"] = 0.24m, ["C"] = 0.24m, ["D"] = 0.24m,
    };

    private static async Task EnsureTopicAsync(CompanionDbContext db, string modality, string code, string name)
    {
        if (await db.Topics.AnyAsync(t => t.Modality == modality && t.Code == code))
        {
            return;
        }

        db.Topics.Add(new Topic { Modality = modality, Code = code, Name = name });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The two topics this fixture needs. English-prompts only, so gate two's coverage floor is met on
    /// the neutral copy below; the other eighteen canonical rows are not seeded here.
    /// </summary>
    private async Task SeedTopicsAsync()
    {
        await ClearAsync();
        await using var db = Db();
        await EnsureTopicAsync(db, "writing", "W-ARG", "Thesis-driven persuasive and opinion argumentation");
        await EnsureTopicAsync(db, "speaking", "S-ARG", "Sustained monologic argumentation and justification");
    }

    private static string Prompt => $"Write an essay answering the question on the board. [{Nonce}]";

    private static ProductiveTaskDraftRequest WritingTask(
        int min = 110,
        int max = 300,
        int? prep = null,
        int? response = 900,
        bool placement = false,
        string? image = null,
        string? alt = null) =>
        new(
            ModalityDto.Writing,
            "B1",
            [
                new ProductiveSubTaskInput(
                    "persuasive_essay",
                    $"Write the essay described on the board. [{Nonce}]",
                    min, max, prep, response, "The essay should state a position and defend it with reasons."),
            ],
            "W-ARG",
            PromptCopy: Prompt,
            ImagePath: image,
            ImageAlt: alt,
            PlacementEligible: placement);

    private static ProductiveTaskDraftRequest SpeakingTask(
        int prep = 60,
        int response = 120,
        bool placement = false) =>
        new(
            ModalityDto.Speaking,
            "B1",
            [
                new ProductiveSubTaskInput(
                    "argumentative_monologue",
                    $"Argue for or against the proposition. [{Nonce}]",
                    null, null, prep, response,
                    "The answer should defend one side with reasons and answer the counter-argument."),
            ],
            "S-ARG",
            PromptCopy: Prompt,
            PlacementEligible: placement);

    // -----------------------------------------------------------------------------------------
    // AC1: a task round-trips into the pool with its prompt, its windows and its word range
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input this guard must not pass.</b> A save that writes the item and version but drops the
    /// four new columns: the DTO would show the range the author typed, the row would carry NULLs, and
    /// the sitting would render "no target" over a task the author believes is bounded. Asserting the
    /// <em>stored row</em> is what catches it.
    /// </summary>
    [Fact]
    public async Task A_writing_task_saves_its_prompt_its_word_range_and_its_windows_on_the_version()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var saved = await Service(db).SaveAsync(WritingTask(min: 110, max: 300, prep: 300, response: 900));

        var part = Assert.Single(saved.Parts);
        // #256: gate four's classifier RAN here (the service is built with models), so the task is
        // staged `validated` - the state that means "gates passed, awaiting audit".
        Assert.Equal("validated", part.LifecycleStatus);
        Assert.Equal("demo", part.ContentTier);
        Assert.Equal("writing", part.Modality.ToString().ToLowerInvariant());

        await using var check = Db();
        var version = await check.ItemVersions
            .Include(v => v.Item)
            .SingleAsync(v => v.ItemId == part.ItemId);

        Assert.Equal(110, version.MinWords);
        Assert.Equal(300, version.MaxWords);
        Assert.Equal(300, version.PrepSeconds);
        Assert.Equal(900, version.ResponseSeconds);

        // The productive shape: no options and no key (CNT-06), so it is a task and not a broken MCQ.
        Assert.Null(version.OptionA);
        Assert.Null(version.KeyLabel);

        var stimulus = await check.Stimuli.SingleAsync(s => s.StimulusId == part.StimulusId);
        Assert.Contains(Nonce, stimulus.PromptCopy, StringComparison.Ordinal);
        Assert.Equal("B1", stimulus.CefrLevel.Code);
    }

    [Fact]
    public async Task A_speaking_task_saves_its_preparation_and_recording_windows_and_no_word_range()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var saved = await Service(db).SaveAsync(SpeakingTask(prep: 60, response: 90));

        await using var check = Db();
        var version = await check.ItemVersions.SingleAsync(v => v.ItemId == saved.Parts[0].ItemId);

        Assert.Equal(60, version.PrepSeconds);
        Assert.Equal(90, version.ResponseSeconds);
        // A spoken answer's length is its window, not a word count.
        Assert.Null(version.MinWords);
        Assert.Null(version.MaxWords);
        Assert.Equal("argumentative_monologue", (await check.Items.SingleAsync(i => i.ItemId == saved.Parts[0].ItemId)).ItemType);
    }

    /// <summary>
    /// AC2: a two-part writing task is two rows, each with its OWN range and budget. <b>The input that
    /// would pass a lazy guard:</b> writing both parts from one shared draft, which produces two rows
    /// carrying the same numbers - a two-task sitting then weights Task 2 twice while scoring it
    /// against Task 1's demand.
    /// </summary>
    [Fact]
    public async Task A_two_part_writing_task_saves_two_rows_each_with_its_own_range_and_budget()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [
                new ProductiveSubTaskInput(
                    "transactional_correspondence",
                    $"Write the email described on the board. [{Nonce}]",
                    110, 150, null, 480,
                    "The email should cover all three required actions."),
                new ProductiveSubTaskInput(
                    "persuasive_essay",
                    $"Write the essay described on the board. [{Nonce}]",
                    110, 300, null, 1800,
                    "The essay should state a position and defend it with reasons."),
            ],
            "W-ARG",
            PromptCopy: Prompt);

        var saved = await Service(db).SaveAsync(request);

        Assert.Equal(2, saved.Parts.Count);

        await using var check = Db();
        var versions = await check.ItemVersions
            .Where(v => saved.Parts.Select(p => p.ItemId).Contains(v.ItemId))
            .OrderBy(v => v.ItemVersionId)
            .ToListAsync();

        Assert.Equal(2, versions.Count);
        Assert.Equal(480, versions[0].ResponseSeconds);
        Assert.Equal(150, versions[0].MaxWords);
        Assert.Equal(1800, versions[1].ResponseSeconds);
        Assert.Equal(300, versions[1].MaxWords);

        // One stimulus, two parts: a prompt per part would be two tasks.
        var stimulusIds = await check.Items
            .Where(i => saved.Parts.Select(p => p.ItemId).Contains(i.ItemId))
            .Select(i => i.StimulusId)
            .Distinct()
            .ToListAsync();
        Assert.Single(stimulusIds);
    }

    // -----------------------------------------------------------------------------------------
    // AC2: a two-part task saved with one part present is refused
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A "two-part" writing task whose second part was
    /// never filled in: a count check that only refuses <em>three</em> parts accepts it, and the
    /// sitting scores a task whose second half has no prompt. The refusal names the part.
    /// </summary>
    [Fact]
    public async Task A_two_part_writing_task_with_an_empty_second_part_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [
                new ProductiveSubTaskInput("transactional_correspondence", $"Write the email. [{Nonce}]", 110, 150, null, 480,
                    "The email should cover all three required actions."),
                new ProductiveSubTaskInput("persuasive_essay", "   ", 110, 300, null, 1800,
                    "The essay should state a position and defend it with reasons."),
            ],
            "W-ARG",
            PromptCopy: Prompt);

        var error = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));
        Assert.Contains("sub-task 2", error.Message, StringComparison.Ordinal);

        await AssertNothingWrittenAsync();
    }

    // -----------------------------------------------------------------------------------------
    // AC3: topic and level come from the studio's context and the save is refused without them
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_task_with_no_topic_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = WritingTask() with { TopicCode = "  " };

        var error = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));
        Assert.Contains(ProductiveTaskRules.Context, error.Message, StringComparison.Ordinal);

        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task A_task_at_no_level_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var error = await Assert.ThrowsAsync<AdminItemException>(
            () => Service(db).SaveAsync(WritingTask() with { Level = "" }));

        Assert.Contains("authored CEFR level", error.Message, StringComparison.Ordinal);
        await AssertNothingWrittenAsync();
    }

    /// <summary>
    /// The level is not decorative: the band's writing floor is what a range is judged against, so a
    /// task whose range is legal at A2 and illegal at B1 must be refused when saved as B1 - proving the
    /// studio read the level from the context rather than from the request's range.
    /// </summary>
    [Fact]
    public async Task A_range_below_the_context_bands_demand_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var error = await Assert.ThrowsAsync<AdminItemException>(
            () => Service(db).SaveAsync(WritingTask(min: 40, max: 90)));

        Assert.Contains(ProductiveTaskRules.WordRange, error.Message, StringComparison.Ordinal);
        await AssertNothingWrittenAsync();
    }

    // -----------------------------------------------------------------------------------------
    // AC4: the placement cap, and that it is shown BEFORE the save
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A C1 placement speaking task with a 120-second
    /// window - legal practice stock under D0.2, refused only because it is placement. The test asserts
    /// the save was <em>refused and wrote nothing</em>, not merely that a message mentioned 60.
    /// </summary>
    [Fact]
    public async Task A_placement_speaking_task_over_the_cap_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = SpeakingTask(response: 120, placement: true) with { Level = "C1" };

        var error = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));
        Assert.Contains(ProductiveTaskRules.PlacementCap, error.Message, StringComparison.Ordinal);
        Assert.Contains("60", error.Message, StringComparison.Ordinal);

        await AssertNothingWrittenAsync();
    }

    /// <summary>
    /// AC4's real requirement: the cap is on the <em>analysis</em>, so the screen can print it beside
    /// the recording field before the author types a prompt. A cap that appeared only in a refusal is
    /// the defect this field exists to prevent.
    /// </summary>
    [Fact]
    public async Task The_analysis_reports_the_placement_recording_cap_before_any_save()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var analysis = await Service(db).AnalyseAsync(SpeakingTask(response: 60, placement: true));

        Assert.Equal(60, analysis.PlacementRecordingCap);
        Assert.True(analysis.Acceptable);

        // And the same task as practice stock carries no cap, because practice is not bounded.
        var practice = await Service(db).AnalyseAsync(SpeakingTask(response: 120));
        Assert.Null(practice.PlacementRecordingCap);
        Assert.True(practice.Acceptable);
    }

    [Fact]
    public async Task The_analysis_reports_the_scoped_placement_word_range_for_writing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = WritingTask(min: 120, max: 140, response: 900, placement: true) with { Level = "C1" };

        // No models: gate four's classifier is not what this test is about, and its "did not run" is
        // reported as an unrun check rather than as a refusal.
        var analysis = await Service(db, withModels: false).AnalyseAsync(request);

        Assert.True(analysis.Acceptable);
        var scoped = Assert.Single(analysis.PlacementWordRanges);
        Assert.Equal((120, 140), (scoped.Floor, scoped.Ceiling));
        Assert.True(scoped.PlacementScoped);

        // The practice figure for the same band is the wider one, and the screen shows both.
        var practice = Assert.Single(analysis.PracticeWordRanges);
        Assert.Equal((250, 300), (practice.Floor, practice.Ceiling));
    }

    // -----------------------------------------------------------------------------------------
    // the double-check: an item that reaches the pool with no rubric anchor
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> Running gate three against a productive task:
    /// the seam would hand two blind verifiers an empty option list, gate three would report
    /// <c>UnusableDistribution</c> for a key that does not exist, and - because that code is in the
    /// "this check has not run" list - the screen would print "gate three did not run" over a task gate
    /// three has nothing to say about. The analysis must name no gate-three check at all: not run and
    /// not applicable are different facts, and only the second is true here.
    /// </summary>
    [Fact]
    public async Task A_productive_task_names_no_gate_three_check_because_gate_three_is_not_applicable()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var analysis = await Service(db).AnalyseAsync(WritingTask());

        Assert.True(analysis.Acceptable);
        Assert.DoesNotContain(analysis.UnrunChecks, check => check.Rule.StartsWith("gate3.", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.Refusals, refusal => refusal.Rule.StartsWith("gate3.", StringComparison.Ordinal));
    }

    /// <summary>
    /// The control: an <em>objective</em> bundle through the same unwired deployment does name gate
    /// three's absence, so the assertion above is about the productive branch rather than about a list
    /// that is empty everywhere. Same seam, same fake-less gates, different skill.
    /// </summary>
    [Fact]
    public async Task An_objective_bundle_in_the_same_deployment_does_name_gate_three_as_not_run()
    {
        await SeedTopicsAsync();
        await using var db = Db();
        await EnsureTopicAsync(db, "reading", "R-DET", "Local factual retrieval and paraphrase matching");

        var reading = new StimulusStudioService(db, TimeProvider.System, new PoolIngestionGates(db));
        var analysis = await reading.AnalyseAsync(new StimulusDraftRequest(
            ModalityDto.Reading,
            "B1",
            "passage_comprehension",
            [
                new StimulusDraftChild(1, "B1", "R-DET", $"What opens at six? [{Nonce}]",
                    "The depot.", "The school.", "The clinic.", "The bank.", "A",
                    "The passage names the depot.",
                    "Correct: the passage states it.",
                    "The passage never mentions a school.",
                    "No clinic appears.",
                    "A bank is not mentioned.",
                    EvidenceStart: 0, EvidenceEnd: 14),
                new StimulusDraftChild(2, "B1", "R-DET", $"Who signs the ledger? [{Nonce}]",
                    "The staff.", "The pupils.", "The drivers.", "The visitors.", "A",
                    "The passage names the staff.",
                    "Correct: the passage states it.",
                    "The passage never mentions pupils.",
                    "No driver appears.",
                    "Visitors are not mentioned.",
                    EvidenceStart: 20, EvidenceEnd: 40),
            ],
            Passage: "The depot opens at six. Every member of staff signs the night ledger before leaving."));

        Assert.Contains(
            analysis.UnrunChecks,
            check => check.Rule.StartsWith("gate3.", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------------------------
    // the seam's own refusals still fire: no gate is re-implemented or bypassed
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A task whose level claim an independent
    /// classifier disagrees with is refused by gate four - which is only true if the studio really
    /// called the seam rather than building rows and saving them. The fake predicts B2; the task is
    /// authored B1.
    /// </summary>
    [Fact]
    public async Task A_task_whose_level_the_classifier_disagrees_with_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var gates = new PoolIngestionGates(
            db,
            classifier: new FakeLevelClassifier("classifier-family", "c1") { Predicted = CefrLevel.B2 });

        var error = await Assert.ThrowsAsync<AdminItemException>(
            () => new ProductiveStudioService(db, TimeProvider.System, gates).SaveAsync(WritingTask()));

        Assert.Contains(GateFourRules.LevelDisagreement, error.Message, StringComparison.Ordinal);
        await AssertNothingWrittenAsync();
    }

    /// <summary>
    /// With no classifier configured, gate four reports that its check did not run rather than passing
    /// or refusing. The save still writes a draft - a draft is not a certification (CNT-42) - and the
    /// analysis names the check, so the screen does not claim a certification nobody made.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_deployment_still_saves_a_draft_and_names_the_check_that_did_not_run()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var service = Service(db, withModels: false);
        var analysis = await service.AnalyseAsync(WritingTask());

        Assert.True(analysis.Acceptable);
        Assert.Contains(analysis.UnrunChecks, check => check.Rule == GateFourRules.ClassifierUnavailable);

        var saved = await service.SaveAsync(WritingTask(min: 110, max: 300, prep: 400, response: 1200));
        var part = Assert.Single(saved.Parts);
        // #256: no classifier, so gate four did not run and the task correctly stays `draft`. Staging it
        // `validated` would claim a certification nobody made.
        Assert.Equal("draft", part.LifecycleStatus);
    }

    /// <summary>
    /// The tier prefix is CNT-03’s, and the studio cannot mint production stock: an id that says one
    /// tier while the column says another is refused by the seam, not by a copy of the rule here.
    /// <para>
    /// #256: the lifecycle is now decided by whether the gates ran, not by the request - and the request
    /// still has no field that can say. A <c>validated</c> stage here is the wired deployment; the
    /// unwired one is asserted <c>draft</c> in the test above, which is the half that keeps this honest.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_saved_rows_are_demo_and_validated_and_no_request_field_can_say_otherwise()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var saved = await Service(db).SaveAsync(WritingTask());

        await using var check = Db();
        var item = await check.Items.SingleAsync(i => i.ItemId == saved.Parts[0].ItemId);
        Assert.Equal("demo", item.ContentTier);
        Assert.Equal("validated", item.LifecycleStatus);

        // The request shape carries no tier and no lifecycle at all, which is what makes the two
        // constants above the only reachable answer.
        var names = typeof(ProductiveTaskDraftRequest).GetProperties()
            .Select(property => property.Name)
            .ToList();
        Assert.DoesNotContain("ContentTier", names);
        Assert.DoesNotContain("LifecycleStatus", names);
        Assert.DoesNotContain("ExamId", names);
    }

    // -----------------------------------------------------------------------------------------
    // the visual stimulus
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_data_synthesis_task_without_its_visual_stimulus_is_refused_and_writes_nothing()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [new ProductiveSubTaskInput("visual_data_synthesis", $"Summarise the chart. [{Nonce}]", 110, 180, null, 600, "The summary should name the trend and the two largest quarters.")],
            "W-ARG",
            PromptCopy: Prompt);

        var error = await Assert.ThrowsAsync<AdminItemException>(() => Service(db).SaveAsync(request));
        Assert.Contains(ProductiveTaskRules.VisualStimulus, error.Message, StringComparison.Ordinal);

        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task A_data_synthesis_task_with_its_visual_stimulus_stores_the_image_and_its_alt_text()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var request = new ProductiveTaskDraftRequest(
            ModalityDto.Writing,
            "B1",
            [new ProductiveSubTaskInput("visual_data_synthesis", $"Summarise the chart. [{Nonce}]", 110, 180, null, 600, "The summary should name the trend and the two largest quarters.")],
            "W-ARG",
            PromptCopy: Prompt,
            ImagePath: "img/chart-2.svg",
            ImageAlt: "A bar chart of quarterly revenue.");

        var saved = await Service(db).SaveAsync(request);

        await using var check = Db();
        var stimulus = await check.Stimuli.SingleAsync(s => s.StimulusId == saved.Parts[0].StimulusId);
        Assert.Equal("img/chart-2.svg", stimulus.ImagePath);
        Assert.Equal("A bar chart of quarterly revenue.", stimulus.ImageAlt);
    }

    // -----------------------------------------------------------------------------------------
    // the pickers
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_options_offer_the_four_types_of_the_skill_and_no_other()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        var options = await Service(db).GetOptionsAsync("writing");

        Assert.Equal(4, options.Types.Count);
        Assert.Contains(options.Types, type => type.Code == "persuasive_essay");
        Assert.Contains(options.Types, type => type.Code == "visual_data_synthesis" && type.Visual == "required");
        Assert.DoesNotContain(options.Types, type => type.Code == "descriptive_monologue");
        Assert.Contains(options.Topics, topic => topic.Code == "W-ARG");
        Assert.DoesNotContain(options.Topics, topic => topic.Code == "S-ARG");
    }

    [Fact]
    public async Task An_objective_skill_is_refused_by_the_productive_studio()
    {
        await SeedTopicsAsync();
        await using var db = Db();

        await Assert.ThrowsAsync<AdminItemException>(() => Service(db).GetOptionsAsync("reading"));
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// No row this fixture could have written exists. A refusal that returns the right sentence and
    /// writes half a task anyway is the failure mode a "the exception was thrown" assertion misses.
    /// </summary>
    private async Task AssertNothingWrittenAsync()
    {
        await using var check = Db();
        var like = $"%{Nonce}%";
        Assert.Equal(0, await check.Stimuli.CountAsync(s => s.PromptCopy != null && EF.Functions.Like(s.PromptCopy, like)));
        Assert.Equal(0, await check.ItemVersions.CountAsync(v => EF.Functions.Like(v.Stem, like)));
    }
}


