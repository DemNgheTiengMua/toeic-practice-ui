using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// #209: populates one cell through the authoring pipeline, in the database the running app reads.
/// <para>
/// <b>Not a seed.</b> Every row below is produced by the production seams in order - the workbench
/// upload, the four gates, <c>validated</c>, the audit's draw/solve/reveal/verdict/decide, then
/// <c>active</c>. A helper that inserted <c>active</c> directly would make the journey pass while
/// proving nothing, which is the trap the ticket names.
/// </para>
/// <para>
/// <b>Why the gates get a scripted verifier and classifier.</b> This deployment registers no
/// <c>IVerifierModel</c> and no <c>ILevelClassifier</c>, so its gates refuse every record (measured:
/// <c>gate3.distribution.unusable</c>, <c>gate4.level.unavailable</c>). The pipeline is proved with
/// the models a configured deployment supplies; what is missing is the wiring, not the pipeline, and
/// the journey says so rather than pretending the environment certified anything.
/// </para>
/// </summary>
internal static class OneCellFixture
{
    internal sealed record Populated(int BatchId, int StepId, int ActiveItems, string Email, string Password);

    /// <summary>The password the journey learner is created with, so the test can sign in as it.</summary>
    private const string JourneyPassword = "Demo1234!";

    private const string KeyMarker = "ledger";

    private static readonly (string Text, string Rationale)[] OptionTexts =
    [
        ("The night ledger.", "Correct: the notice names the ledger."),
        ("The depot shutter.", "The passage never mentions a shutter."),
        ("The depot supervisor.", "No supervisor appears here."),
        ("The office doorway.", "A door is not what the notice requires."),
    ];

    /// <summary>The passage gate two profiles and gate four measures spans against.</summary>
    private const string Passage =
        "The city library will move to a new building next month, and the staff have spent the winter "
        + "preparing for the change. The old building on Mill Street opened in 1972 and it has been too "
        + "small for many years. Because the number of members has grown quickly, the main reading room is "
        + "often full by ten in the morning. The new building on the river side has three floors, a large "
        + "room for children and a small cafe near the door. It also has a quiet room where students can "
        + "study for their exams, and the room can be booked for two hours. The library bought the land in "
        + "2019, but the work stopped for a year because the cost of building rose sharply. The city council "
        + "found the extra money in 2021 and the builders returned to the site in the spring. Most of the "
        + "books are already in boxes, so the old building will close two weeks before the move. Readers who "
        + "need a book during that time can ask the staff, and a van will bring it to the town hall. The "
        + "library will also keep its online service open during the move, so members can still borrow "
        + "digital books. The staff are proud of the new building, which has larger windows and much better "
        + "heating than the old one. They hope that more people will come in the evening, when the library "
        + "will stay open until nine. A small cafe will be run by a local family, because the money it makes "
        + "will go back into the fund. Children who join the library this summer will get a free book, which "
        + "they may keep. The van is ready because the shelves are full. The reading room is quiet because "
        + "the exam period has ended.";

    /// <summary>
    /// Populates <paramref name="topicCode"/> at <paramref name="level"/> and returns the journey
    /// learner and the path step whose coordinate is that cell, so the browser can start a drill
    /// without a cell picker the product does not have yet.
    /// </summary>
    internal static async Task<Populated> PopulateReadingCellAsync(string topicCode, string level)
    {
        var connectionString = PlaywrightTestBase.AppDatabaseConnectionString();
        var topicId = await TopicIdAsync(connectionString, topicCode);
        var quota = await QuotaAsync(connectionString, "reading", level);

        // A previous run may have left the cell populated. The cell is only filled again when it holds
        // nothing, so a re-run does not author twice the quota - the upload would be refused by the
        // cell's own balance rule and prove nothing about this run.
        var alreadyActive = await ActiveCountAsync(connectionString, topicId, level);
        int batchId;
        if (alreadyActive >= quota)
        {
            await using var existing = Db(connectionString);
            batchId = await existing.ImportBatches
                .Where(b => b.CertId == null && b.SourceName == "journey-209.json")
                .OrderByDescending(b => b.BatchId)
                .Select(b => b.BatchId)
                .FirstOrDefaultAsync();
        }
        else
        {
            // Two distinct staff accounts: ADM-02 refuses a reviewer who uploaded the batch, and the
            // demo seed has one. The second is created here and given the same review permission.
            var (uploaderId, reviewerId) = await StaffAsync(connectionString);
            batchId = await UploadAsync(connectionString, uploaderId, topicCode, level, quota, topicId);
            await DecideAsync(connectionString, batchId, reviewerId, quota);
        }

        var active = await ActiveCountAsync(connectionString, topicId, level);

        // A learner of this journey's own, with a one-step path at this cell. The demo learner's path
        // is shared with every other journey, and answering a step closes it permanently (a closed
        // step cannot be reopened - ADR-0012), so driving theirs would change their screen for good.
        var (email, stepId) = await JourneyLearnerAsync(connectionString, topicId, level);

        return new Populated(batchId, stepId, active, email, JourneyPassword);
    }

    /// <summary>
    /// A learner whose active certificate is the demo one and whose path is a copy of the demo learner's
    /// own seeded path, made once and reused by a fixed email.
    /// <para>
    /// <b>Why a copy rather than a bespoke path.</b> The plan read is not just the steps: a path carries
    /// its placement, its window and its weeks, and <c>AdvanceWindowAsync</c> completes a path whose
    /// window it cannot advance. A hand-built path therefore reads as "no active path" even with correct
    /// steps. Copying the seeded path keeps every one of those facts real, and gives this journey a
    /// learner of its own - closing one of <em>its</em> steps advances <em>its</em> week, never the demo
    /// learner's, which other journeys read.
    /// </para>
    /// </summary>
    private static async Task<(string Email, int StepId)> JourneyLearnerAsync(
        string connectionString, int topicId, string level)
    {
        await using var db = Db(connectionString);

        var template = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "learner@demo.local");
        var email = "cell209.journey@demo.local";

        var learner = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (learner is not null)
        {
            // An earlier shape of this fixture may have left a path the window advanced to `completed`.
            // A completed path is not one the learner page renders, so it is retired and rebuilt below.
            var stale = await db.LearnerPaths
                .Where(p => p.UserId == learner.UserId && p.Status != "active")
                .ToListAsync();

            if (stale.Count > 0)
            {
                foreach (var old in stale)
                {
                    old.Status = "reset";
                }

                await db.SaveChangesAsync();
            }

            var live = await db.LearnerPaths
                .AnyAsync(p => p.UserId == learner.UserId && p.Status == "active");

            if (live)
            {
                // Only a live path with its steps already cloned is reusable; a bare path (an earlier
                // shape of this fixture, or a half-written run) is rebuilt below instead.
                var existingStep = await StartableCellStepAsync(db, learner.UserId, topicId, level, createIfMissing: false);
                if (existingStep is not null)
                {
                    return (email, existingStep.Value);
                }

                await db.LearnerPaths
                    .Where(p => p.UserId == learner.UserId && p.Status == "active")
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "reset"));
            }
        }

        if (learner is null)
        {
            learner = new User
            {
                RoleId = template.RoleId,
                Email = email,
                PasswordHash = Convert.FromBase64String(
                    new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(
                        new User { Email = email }, JourneyPassword)),
                FullName = "Cell 209 journey learner",
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                ActiveCertId = template.ActiveCertId,
            };

            db.Users.Add(learner);
            await db.SaveChangesAsync();
        }

        var templatePath = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == template.UserId && p.Status == "active")
            .OrderByDescending(p => p.LearnerPathId)
            .FirstAsync();

        // The path names its placement, and the placement is the learner's own (a composite FK on
        // (PlacementId, UserId)). A `done` placement also requires an attempt
        // (CK_Placement_DoneHasAttempt), so the journey learner gets an in-progress copy carrying the
        // same band snapshot: enough for the page to render a path, without claiming a sitting that
        // this learner never took.
        int? placementId = null;
        if (templatePath.PlacementId is { } sourcePlacementId)
        {
            var source = await db.PlacementTests.AsNoTracking().SingleAsync(p => p.PlacementId == sourcePlacementId);

            // An in-progress placement derives nothing, so it declares no writer (ADR-0012). The result
            // snapshot is deliberately not copied: this learner never sat it.
            var copy = new PlacementTest
            {
                UserId = learner.UserId,
                CertId = source.CertId,
                ExamId = source.ExamId,
                Status = "in_progress",
                ResultBandCode = null,
                AttemptId = null,
                ExamPurpose = source.ExamPurpose,
                TotalScore = null,
                TakenAt = DateTimeOffset.UtcNow,
            };

            db.PlacementTests.Add(copy);
            await db.SaveChangesAsync();
            placementId = copy.PlacementId;
        }

        var path = new LearnerPath
        {
            UserId = learner.UserId,
            CertId = templatePath.CertId,
            CurrentBandCode = templatePath.CurrentBandCode,
            PlacementId = placementId,
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow,
            WindowStartDate = templatePath.WindowStartDate,
            ActiveWeek = 1,
            WindowSlides = templatePath.WindowSlides,
        };

        db.LearnerPaths.Add(path);
        await db.SaveChangesAsync();

        var templateSteps = await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPathId == templatePath.LearnerPathId)
            .OrderBy(s => s.StepId)
            .ToListAsync();

        foreach (var source in templateSteps)
        {
            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = path.LearnerPathId,
                CertId = source.CertId,
                SkillId = source.SkillId,
                Modality = source.Modality,
                TopicId = source.TopicId,
                TargetCefr = source.TargetCefr,
                StepType = source.StepType,
                SizingTier = source.SizingTier,
                RefLessonId = source.RefLessonId,
                RefExamId = source.RefExamId,
                // `done` is a derived fact with one writer (PathStepCompletion), so a copy must not
                // claim it: the journey's own step is open and everything else waits behind it.
                Status = source.TopicId == topicId && source.TargetCefr == level ? "available" : "locked",
                WeekNumber = source.WeekNumber,
                WeekSequence = source.WeekSequence,
                PlannedItemCount = source.PlannedItemCount,
                EstimatedMinutes = source.EstimatedMinutes,
                TargetAccuracyPct = source.TargetAccuracyPct,
            });
        }

        await db.SaveChangesAsync();

        var stepId = await StartableCellStepAsync(db, learner.UserId, topicId, level, createIfMissing: true)
            ?? throw new InvalidOperationException(
                $"the seeded path holds no step at topic {topicId} {level}, so the copied path has none either");

        return (email, stepId);
    }

    /// <summary>
    /// The first startable step of this learner's path at the cell, or a new one when an earlier run
    /// closed the last of them (ADR-0012 refuses to reopen a closed step).
    /// </summary>
    private static async Task<int?> StartableCellStepAsync(
        CompanionDbContext db, int userId, int topicId, string level, bool createIfMissing)
    {
        var step = await db.LearnerPathSteps
            .Where(s => s.LearnerPath.UserId == userId
                && s.TopicId == topicId
                && s.TargetCefr == level
                && (s.Status == "available" || s.Status == "locked"))
            .OrderBy(s => s.StepId)
            .FirstOrDefaultAsync();

        if (step is not null)
        {
            if (step.Status == "locked")
            {
                step.Status = "available";
                await db.SaveChangesAsync();
            }

            return step.StepId;
        }

        if (!createIfMissing)
        {
            return null;
        }

        var path = await db.LearnerPaths
            .Where(p => p.UserId == userId && p.Status == "active")
            .OrderByDescending(p => p.LearnerPathId)
            .FirstOrDefaultAsync();

        if (path is null)
        {
            return null;
        }

        var template = await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPath.UserId == userId)
            .OrderByDescending(s => s.StepId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"the journey learner has no step to copy a skill from at topic {topicId} {level}");

        var sequence = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == path.LearnerPathId)
            .MaxAsync(s => (int?)s.WeekSequence) ?? 0;

        var created = new LearnerPathStep
        {
            LearnerPathId = path.LearnerPathId,
            CertId = path.CertId,
            SkillId = template!.SkillId,
            Modality = "reading",
            TopicId = topicId,
            TargetCefr = level,
            StepType = "focus_practice",
            SizingTier = "developing",
            Status = "available",
            WeekNumber = path.ActiveWeek,
            WeekSequence = sequence + 1,
            EstimatedMinutes = 15,
            TargetAccuracyPct = 70,
        };

        db.LearnerPathSteps.Add(created);
        await db.SaveChangesAsync();
        return created.StepId;
    }

    /// <summary>
    /// Reopens today for the journey learner without deleting rows other tables reference: the session
    /// an AI insight is written against refuses a delete, and both derived locks (the daily ceiling and
    /// the per-cell day lock) read the answers' content, not the session row.
    /// </summary>
    internal static async Task ResetJourneyLearnerAsync(string email)
    {
        var connectionString = PlaywrightTestBase.AppDatabaseConnectionString();
        await using var db = Db(connectionString);

        var userId = await db.Users.Where(u => u.Email == email).Select(u => (int?)u.UserId).FirstOrDefaultAsync();
        if (userId is null)
        {
            return;
        }

        var sessions = await db.PracticeSessions.Where(s => s.UserId == userId.Value).ToListAsync();
        foreach (var answer in await db.PracticeAnswers
                     .Where(a => a.Session.UserId == userId.Value && a.AnsweredAt != null)
                     .ToListAsync())
        {
            answer.AnsweredAt = null;
            answer.IsCorrect = null;
            answer.SelectedLabel = null;
            answer.SelectedOptionId = null;
        }

        foreach (var session in sessions)
        {
            session.Status = "abandoned";
        }

        await db.SaveChangesAsync();
    }

    private static CompanionDbContext Db(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new CompanionDbContext(options);
    }

    /// <summary>
    /// A context that declares who is writing, as the API's registration does. The crawl database is
    /// <b>not</b> a fixture catalog, so <c>trg_Items_TriageCannotWrite</c> refuses a lifecycle write
    /// that names nobody - which is the production rule, not an obstacle to route around.
    /// </summary>
    private static CompanionDbContext DbAs(string connectionString, int actingUserId)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new StaffAccountStampInterceptor(new Acting(actingUserId)))
            .Options;
        return new CompanionDbContext(options);
    }

    private sealed class Acting(int userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    private static async Task<int> TopicIdAsync(string connectionString, string topicCode)
    {
        await using var db = Db(connectionString);
        return await db.Topics
            .Where(t => t.Modality == "reading" && t.Code == topicCode)
            .Select(t => t.TopicId)
            .SingleAsync();
    }

    private static async Task<int> QuotaAsync(string connectionString, string modality, string level)
    {
        await using var db = Db(connectionString);
        return await db.ContentQuotas
            .Where(q => q.Modality == modality && q.CefrLevel == level)
            .Select(q => q.TargetItemCount)
            .SingleAsync();
    }

    private static async Task<int> ActiveCountAsync(string connectionString, int topicId, string level)
    {
        if (!CefrLevel.TryParseAuthored(level, out var parsed))
        {
            throw new InvalidOperationException($"'{level}' is not an authored CEFR level");
        }

        await using var db = Db(connectionString);
        return await db.Items.AsNoTracking()
            .CountAsync(i => i.TopicId == topicId && i.CefrLevel == parsed && i.LifecycleStatus == "active");
    }

    /// <summary>
    /// Two acting staff accounts holding author and review. The demo admin holds both namespaces, but
    /// ADM-02 is about <em>identity</em>, not permission - one account cannot clear what it uploaded -
    /// so the reviewer has to be somebody else.
    /// </summary>
    private static async Task<(int Uploader, int Reviewer)> StaffAsync(string connectionString)
    {
        await using var db = Db(connectionString);

        var role = new Role { Code = $"j209-{Guid.NewGuid():N}"[..20], Name = "Journey 209 staff" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        foreach (var code in new[] { Permissions.ContentAuthor, Permissions.ContentReview })
        {
            if (!await db.Permissions.AnyAsync(p => p.Code == code))
            {
                db.Permissions.Add(new Permission { Code = code });
            }

            db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionCode = code });
        }

        await db.SaveChangesAsync();

        var uploader = NewUser(role.RoleId, "j209-uploader");
        var reviewer = NewUser(role.RoleId, "j209-reviewer");
        db.Users.AddRange(uploader, reviewer);
        await db.SaveChangesAsync();

        return (uploader.UserId, reviewer.UserId);
    }

    private static User NewUser(int roleId, string tag) => new()
    {
        RoleId = roleId,
        Email = $"{tag}-{Guid.NewGuid():N}@journey209.local",
        PasswordHash = [1],
        FullName = tag,
        EmailVerifiedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<int> UploadAsync(
        string connectionString, int uploaderId, string topicCode, string level, int quota, int topicId)
    {
        await using var db = DbAs(connectionString, uploaderId);

        // The cell's existing key distribution, which gate one judges every arrival against.
        var existing = await ExistingKeysAsync(db, topicId, level);
        var keys = BalancedKeys(existing, quota);

        var gates = new PoolIngestionGates(
            db,
            verifierOne: new MarkerVerifier("verifier-a", "v1"),
            verifierTwo: new MarkerVerifier("verifier-b", "v2"),
            ablationSolver: null,
            classifier: new FakeLevelClassifier("classifier-family", "c1")
            {
                Predicted = CefrLevel.TryParseAuthored(level, out var parsed) ? parsed : throw new InvalidOperationException(level),
            });

        var workbench = new IngestionWorkbenchService(
            db, TimeProvider.System, new PoolIngestionService(db, TimeProvider.System, gates));

        var file = File(topicCode, level, keys);
        var uploaded = await workbench.UploadAsync(
            "reading", new MemoryStream(Encoding.UTF8.GetBytes(file)), "journey-209.json", uploaderId);

        if (uploaded.Accepted.Count != quota || uploaded.Rejected.Count != 0)
        {
            throw new InvalidOperationException(
                $"the upload was expected to accept {quota} records and accepted {uploaded.Accepted.Count} "
                + $"(refused {uploaded.Rejected.Count}): "
                + string.Join("; ", uploaded.Rejected.SelectMany(r => r.Refusals.Select(x => x.Rule))));
        }

        return uploaded.BatchId;
    }

    /// <summary>The key letters already stored in the cell, one per item's newest version.</summary>
    private static async Task<IReadOnlyList<int>> ExistingKeysAsync(CompanionDbContext db, int topicId, string level)
    {
        var levelCode = CefrLevel.TryParseAuthored(level, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"'{level}' is not an authored CEFR level");

        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => v.Item.TopicId == topicId && v.Item.CefrLevel == levelCode)
            .Select(v => new { v.ItemId, v.VersionNumber, v.KeyLabel })
            .ToListAsync();

        var counts = new int[4];
        foreach (var version in versions
                     .GroupBy(v => v.ItemId)
                     .Select(g => g.OrderByDescending(v => v.VersionNumber).First()))
        {
            var index = "ABCD".IndexOf(version.KeyLabel ?? string.Empty, StringComparison.Ordinal);
            if (index >= 0)
            {
                counts[index]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// The next <paramref name="count"/> key letters, chosen so the cell stays inside gate one's share
    /// band at every step. The band is read from <see cref="GateOne"/>'s own constants, never restated.
    /// </summary>
    private static IReadOnlyList<string> BalancedKeys(IReadOnlyList<int> existing, int count)
    {
        var counts = existing.ToArray();
        var keys = new List<string>(count);

        for (var step = 0; step < count; step++)
        {
            var chosen = Enumerable.Range(0, 4)
                .OrderBy(letter => counts[letter])
                .FirstOrDefault(letter =>
                {
                    var after = counts.ToArray();
                    after[letter]++;
                    return !CanBeBalanced(after.Sum()) || InBand(after);
                }, -1);

            // No letter can repair a cell that its history unbalanced: the gate notices that case
            // instead of refusing it, and the least-used letter is the honest pick.
            if (chosen < 0)
            {
                chosen = Enumerable.Range(0, 4).OrderBy(letter => counts[letter]).First();
            }

            counts[chosen]++;
            keys.Add("ABCD"[chosen].ToString());
        }

        return keys;
    }

    private static bool CanBeBalanced(int total)
    {
        if (total <= 0)
        {
            return false;
        }

        var low = (total * GateOne.KeyShareFloorPercent + 99) / 100;
        var high = total * GateOne.KeyShareCeilingPercent / 100;
        return low <= high && 4 * low <= total && total <= 4 * high;
    }

    private static bool InBand(IReadOnlyList<int> counts)
    {
        var total = counts.Sum();
        return counts.All(c => c * 100 >= total * GateOne.KeyShareFloorPercent
            && c * 100 <= total * GateOne.KeyShareCeilingPercent);
    }

    private static async Task DecideAsync(string connectionString, int batchId, int reviewerId, int quota)
    {
        await using var db = DbAs(connectionString, reviewerId);
        var audit = new StratifiedAuditService(db, TimeProvider.System);
        var queue = await audit.DrawAsync(batchId, reviewerId);
        var stratum = queue.Strata.Single();

        foreach (var sample in stratum.Items)
        {
            await audit.RecordSolveAsync(sample.SampleItemId, "A", reviewerId);
            await audit.RevealKeyAsync(sample.SampleItemId);
            await audit.RecordVerdictAsync(sample.SampleItemId, AuditVerdict.Correct, reviewerId);
        }

        var decision = await audit.DecideAsync(stratum.StratumId, reviewerId);
        if (decision.Status != "approved")
        {
            throw new InvalidOperationException($"the audit decided '{decision.Status}', not approved");
        }

        if (decision.SampledPromoted + decision.UnsampledPromoted != quota)
        {
            throw new InvalidOperationException(
                $"the decision promoted {decision.SampledPromoted + decision.UnsampledPromoted} of {quota} items");
        }
    }

    // ---- the file ----------------------------------------------------------------------------

    private static string File(string topicCode, string level, IReadOnlyList<string> keys)
    {
        var groups = keys.Select((key, index) => Group(topicCode, level, index, key));
        return $$"""
            {
              "schema_version": "2.0.0",
              "skill": "reading",
              "generated_at": "2026-11-06T00:00:00Z",
              "stimulus_groups": [ {{string.Join(",\n", groups)}} ],
              "productive_tasks": [],
              "lessons": []
            }
            """;
    }

    private static string Group(string topicCode, string level, int index, string key) => $$"""
        {
          "stimulus_id": "stim_demo_j209{{index:D5}}",
          "content_tier": "demo",
          "version": 1,
          "lifecycle_status": "draft",
          "batch_tag": "journey-209",
          "skill": "reading",
          "stimulus_level": "{{level}}",
          "domain_context": "workplace",
          "stimulus_format": "single_passage",
          "content": {
            "text_or_transcript": {{JsonSerializer.Serialize(Passage)}},
            "word_count": 299
          },
          "metrics": {
            "flesch_kincaid_grade": 6.97,
            "mltu_words": 13.0,
            "clauses_per_t_unit": 1.35,
            "target_band_lexical_coverage": 0.95
          },
          "items": [ {{Child(topicCode, level, index, key)}} ]
        }
        """;

    private static string Child(string topicCode, string level, int index, string key)
    {
        var keyIndex = "ABCD".IndexOf(key, StringComparison.Ordinal);
        return $$"""
            {
              "item_id": "item_demo_j209{{index:D5}}",
              "content_tier": "demo",
              "sequence_index": {{index + 1}},
              "question_type": "reading_passage_comprehension_mcq",
              "topic_id": "{{topicCode}}",
              "item_level": "{{level}}",
              "placement_eligible": false,
              "time_limit_sec": 45,
              "stem": "What time does the depot open?",
              "options": {{Options(keyIndex)}},
              "correct_option": "{{key}}",
              "explanation": "The first sentence states the opening time.",
              "evidence_span": { "char_start": 4, "char_end": 21, "verbatim_quote": "opens at six" },
              "verification_audit": {
                "author_model": "author-family",
                "verifier_a_model": "verifier-a",
                "verifier_b_model": "verifier-b",
                "verifier_agreement": true,
                "human_reviewed": false
              }
            }
            """;
    }

    /// <summary>
    /// The four options with the marker text rotated into <paramref name="keyIndex"/>, so a balanced
    /// cell still has one option the stub verifier can identify.
    /// </summary>
    private static string Options(int keyIndex)
    {
        var slots = new (string Text, string Rationale)?[4];
        slots[keyIndex] = OptionTexts[0];

        var rest = OptionTexts.Skip(1).ToList();
        var next = 0;
        for (var position = 0; position < 4; position++)
        {
            slots[position] ??= rest[next++];
        }

        var options = slots.Select((slot, position) =>
            $$"""{ "key": "{{("ABCD")[position]}}", "text": {{JsonSerializer.Serialize(slot!.Value.Text)}}, "distractor_rationale": {{JsonSerializer.Serialize(slot!.Value.Rationale)}} }""");

        return "[" + string.Join(",\n", options) + "]";
    }

    /// <summary>
    /// Puts its weight on the option carrying <see cref="KeyMarker"/>, so the gate is exercised on every
    /// letter. <see cref="FakeVerifierModel"/> cannot do this: it returns ONE fixed distribution.
    /// </summary>
    private sealed class MarkerVerifier(string family, string name) : IVerifierModel
    {
        public Task<GateThreeSolve> SolveAsync(
            GateThreeSolveRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Stimulus is null)
            {
                return Task.FromResult(new GateThreeSolve(family, name, new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["A"] = 0.25m, ["B"] = 0.25m, ["C"] = 0.25m, ["D"] = 0.25m,
                }));
            }

            var keyed = request.Options.Single(o => o.Text.Contains(KeyMarker, StringComparison.OrdinalIgnoreCase));
            var distribution = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var option in request.Options)
            {
                distribution[option.Label] = option.Label == keyed.Label ? 0.90m : 0.03m;
            }

            return Task.FromResult(new GateThreeSolve(family, name, distribution));
        }
    }
}
