using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>The row as the readers consume it: the state, the estimate, and when it next comes round.</summary>
public readonly record struct CellMasteryView(
    int TopicId,
    CefrLevel CefrLevel,
    MasteryState State,
    double Probability,
    int ObservationCount,
    int ConsecutiveWrong,
    DateTimeOffset? NextReviewDueAt,
    DateTimeOffset? LastPractisedAt,
    DateTimeOffset? MasteredAt,
    int ProjectionVersion)
{
    public bool IsDue(DateTimeOffset at) => NextReviewDueAt is null || NextReviewDueAt <= at;
}

/// <summary>
/// #237 / D4 / PLC-66..71: <b>the one writer and the one reader of per-cell mastery.</b>
/// <para>
/// <b>Why one type does both.</b> The estimate is a probability with a prior, which SQL Server cannot
/// derive as a computed column from stored counts (the weight is a power, and the state is a function of
/// events rather than of the value). So the writer is a service. It is a <i>legitimate</i> service
/// owner because the arithmetic it calls is a pure function in <see cref="MasteryCellMachine"/> - there
/// is no second implementation of the rule anywhere, and the reader below returns the stored row
/// verbatim rather than recomputing it.
/// </para>
/// <para>
/// <b>It never recomputes at read time.</b> Every read path returns what is stored. A reader that
/// re-derived the state from the estimate would be a second writer in disguise, and it is exactly the
/// design that makes "two consecutive wrong answers" unreachable (PLC-70).
/// </para>
/// <para>
/// ADR-0012: the row is a derived fact registered in <c>DerivedFactOwners</c> as
/// <c>topic_capability</c>, owned by <c>CapabilityWriter</c>. Every write declares that name in the
/// statement and bumps <c>ProjectionVersion</c>, and the trigger refuses anything else.
/// </para>
/// </summary>
public sealed class MasteryService(CompanionDbContext db, TimeProvider? clock = null)
{
    public const string Writer = DerivedFactWriters.TopicCapability;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// Records one graded answer in a cell and returns the state afterwards.
    /// <para>
    /// A cell's <c>n_min</c> follows the topic's modality (PLC-68): 12 objective items or 3 productive
    /// tasks. It is read from the topic rather than passed in, so a caller cannot pick the easier one.
    /// </para>
    /// </summary>
    public async Task<CellMasteryView> RecordAnswerAsync(
        int userId, int topicId, CefrLevel level, bool isCorrect, CancellationToken cancellationToken = default)
    {
        var modality = await ModalityOfAsync(topicId, cancellationToken);
        var row = await TrackedRowAsync(userId, topicId, level, cancellationToken);
        var current = ToSnapshot(row);

        var at = _clock.GetUtcNow();
        var step = MasteryCellMachine.RecordAnswer(
            current, isCorrect, at, MasteryEstimate.MinimumObservationsFor(modality));

        Apply(row, step.Next, at);
        await db.SaveChangesAsync(cancellationToken);

        return ToView(row);
    }

    /// <summary>
    /// PLC-70's <c>maintenance_pass</c>: a mastered cell re-verified. Refused for a cell that is not
    /// mastered, because a maintenance pass on an unmastered cell is a category error rather than a
    /// no-op - the caller has asked the wrong question and would otherwise get a silent success.
    /// </summary>
    public async Task<CellMasteryView> MaintenancePassAsync(
        int userId, int topicId, CefrLevel level, CancellationToken cancellationToken = default)
    {
        var row = await db.UserCellMasteries
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == level,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cell ({topicId}, {level}) has no mastery row, so there is nothing to maintain.");

        var at = _clock.GetUtcNow();
        var step = MasteryCellMachine.MaintenancePass(ToSnapshot(row), at);

        Apply(row, step.Next, at);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    /// <summary>
    /// PLC-71's <c>inactivity_tick</c>, for every mastered cell of one learner.
    /// <para>
    /// <b>The mock-section exception is scoped to the cell, not to the learner.</b> A mock saves a cell
    /// from decay only when it is (a) a <b>mock</b> attempt at this certificate, (b) in <b>this cell's
    /// skill</b>, (c) <b>at or above this cell's level</b>, and (d) sat <b>after</b> the cell was last
    /// practised. "Any mock exists" is not the rule: a below-level mock is not evidence about a B2
    /// cell, and a listening mock is not evidence about a reading one.
    /// </para>
    /// </summary>
    /// <param name="demonstrations">
    /// The mock sections that could demonstrate a cell, already resolved to (skill, level, sat-at). The
    /// caller supplies them so this method needs no knowledge of the attempt tables - the same reason
    /// the machine's <c>InactivityTick</c> takes a bool.
    /// </param>
    public async Task<IReadOnlyList<CellMasteryView>> ApplyInactivityAsync(
        int userId,
        IReadOnlyCollection<MockSectionDemonstration> demonstrations,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.UserCellMasteries
            .Include(m => m.Topic)
            .Where(m => m.UserId == userId
                && m.State == nameof(MasteryState.Mastered).ToLowerInvariant())
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var at = _clock.GetUtcNow();
        var changed = new List<CellMasteryView>(rows.Count);

        foreach (var row in rows)
        {
            var current = ToSnapshot(row);
            var demonstrated = IsDemonstratedByMockSection(row, demonstrations, current.LastPractisedAt);
            var step = MasteryCellMachine.InactivityTick(current, at, demonstrated);

            if (!step.MovedState && step.Next == current)
            {
                // Not yet past the onset, and nothing to re-date: no write, so an idle learner produces
                // no churn and the trigger sees nothing.
                changed.Add(ToView(row));
                continue;
            }

            Apply(row, step.Next, at);
            changed.Add(ToView(row));
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>
    /// The learner's stored mastery of one cell, or a snapshot at the prior when there is no row.
    /// <b>A read, verbatim.</b>
    /// </summary>
    public async Task<CellMasteryView?> ReadAsync(
        int userId, int topicId, CefrLevel level, CancellationToken cancellationToken = default)
    {
        var row = await db.UserCellMasteries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == level,
                cancellationToken);

        return row is null ? null : ToView(row);
    }

    /// <summary>Every cell this learner has answered, keyed by (topic, level) - the practice hub's read.</summary>
    public async Task<IReadOnlyDictionary<(int TopicId, CefrLevel Level), CellMasteryView>> ReadAllAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.UserCellMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(m => (m.TopicId, m.CefrLevel), ToView);
    }

    /// <summary>
    /// Whether a mock section at or above this cell's level, in this cell's skill, demonstrated the
    /// cell after it was last practised.
    /// <para>
    /// Exposed as a pure function so the scoping rule can be asserted without a database: it is the
    /// exact rule the ticket names, and "any mock exists" is the input that passes while being wrong.
    /// </para>
    /// </summary>
    public static bool IsDemonstratedByMockSection(
        int cellTopicId,
        CefrLevel cellLevel,
        Modality cellModality,
        DateTimeOffset? lastPractisedAt,
        IReadOnlyCollection<MockSectionDemonstration> demonstrations) =>
        lastPractisedAt is { } practised
        && demonstrations.Any(d =>
            d.Modality == cellModality
            && d.Level >= cellLevel
            && d.SatAt > practised);

    private static bool IsDemonstratedByMockSection(
        UserCellMastery row,
        IReadOnlyCollection<MockSectionDemonstration> demonstrations,
        DateTimeOffset? lastPractisedAt) =>
        IsDemonstratedByMockSection(
            row.TopicId, row.CefrLevel, EnumCodes.Parse<Modality>(row.Topic.Modality),
            lastPractisedAt, demonstrations);

    private async Task<Modality> ModalityOfAsync(int topicId, CancellationToken cancellationToken) =>
        EnumCodes.Parse<Modality>(await db.Topics
            .AsNoTracking()
            .Where(t => t.TopicId == topicId)
            .Select(t => t.Modality)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Topic {topicId} does not exist."));

    private async Task<UserCellMastery> TrackedRowAsync(
        int userId, int topicId, CefrLevel level, CancellationToken cancellationToken)
    {
        var row = await db.UserCellMasteries
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == level,
                cancellationToken);

        if (row is not null)
        {
            return row;
        }

        row = new UserCellMastery
        {
            UserId = userId,
            TopicId = topicId,
            CefrLevel = level,
            // The machine's own starting point. The SQL default would fill these on INSERT, but the
            // row is read back by ToSnapshot before that, so an unset property parses as no state at
            // all rather than as `untested`.
            State = MasteryState.Untested.ToCode(),
            WeightedCorrect = 0d,
            WeightedObservations = 0d,
            ObservationCount = 0,
            ConsecutiveWrong = 0,
        };
        db.UserCellMasteries.Add(row);
        return row;
    }

    /// <summary>
    /// Writes the machine's outcome onto the row. <c>WrittenByService</c> is marked modified explicitly
    /// because EF omits an unchanged column from the UPDATE and the trigger checks
    /// <c>UPDATE(WrittenByService)</c> - the same convention <c>SkillStateWriter</c> follows. Without it
    /// the second and every later answer to a cell is refused by the single-writer trigger, which is
    /// PRN-01's inherited-name case arriving from the ORM rather than from a second service.
    /// </summary>
    private void Apply(
        UserCellMastery row, MasteryCellMachine.Snapshot next, DateTimeOffset at)
    {
        row.State = next.State.ToCode();
        row.WeightedCorrect = next.Evidence.WeightedCorrect;
        row.WeightedObservations = next.Evidence.WeightedObservations;
        row.ObservationCount = next.Evidence.ObservationCount;
        row.ConsecutiveWrong = next.ConsecutiveWrong;
        row.NextReviewDueAt = next.NextReviewDueAt;
        row.LastPractisedAt = next.LastPractisedAt;
        row.MasteredAt = next.MasteredAt;
        row.UpdatedAt = at;
        row.WrittenByService = Writer;
        row.ProjectionVersion++;

        db.Entry(row).Property(m => m.WrittenByService).IsModified = true;
    }

    private static MasteryCellMachine.Snapshot ToSnapshot(UserCellMastery row) => new(
        EnumCodes.Parse<MasteryState>(row.State),
        new MasteryEvidence(row.WeightedCorrect, row.WeightedObservations, row.ObservationCount),
        row.ConsecutiveWrong,
        row.LastPractisedAt,
        row.MasteredAt,
        row.NextReviewDueAt);

    private static CellMasteryView ToView(UserCellMastery row) => new(
        row.TopicId,
        row.CefrLevel,
        EnumCodes.Parse<MasteryState>(row.State),
        new MasteryEvidence(row.WeightedCorrect, row.WeightedObservations, row.ObservationCount).Probability,
        row.ObservationCount,
        row.ConsecutiveWrong,
        row.NextReviewDueAt,
        row.LastPractisedAt,
        row.MasteredAt,
        row.ProjectionVersion);
}

/// <summary>
/// One mock section that could demonstrate a cell: the skill it examined, the level it was sat at, and
/// when. A record rather than the attempt entity, so the demonstration rule is testable without the
/// attempt tables and the service needs no reach into <c>AttemptEngine</c>'s ownership.
/// </summary>
public readonly record struct MockSectionDemonstration(Modality Modality, CefrLevel Level, DateTimeOffset SatAt);
