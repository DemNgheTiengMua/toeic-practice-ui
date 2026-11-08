using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #238 / SCR-PRAC-01: the objective practice matrix — every <c>(topic, level)</c> cell the learner's
/// certificate examines, with its four-state mastery and the moment it reopens.
/// <para>
/// <b>This is a read, not a selector.</b> It draws no items and returns no draw: it counts the answers
/// the learner has already given and echoes the cooldown those answers imply. Selection stays
/// <see cref="ItemSelector"/>'s alone (#235 removed the two draws and this must not add a third).
/// </para>
/// <para>
/// <b>One definition each.</b> Mastery is <see cref="CellMastery.Estimate"/> — the same function the
/// path sizes a step from. The reopen instant is <see cref="ItemCooldown.CooldownUntil"/> and
/// <see cref="PoolRanking.SoonestServableAt"/> — the same arithmetic the selector excludes cooling
/// items with. Reading a second copy of either would let the matrix and the drill disagree.
/// </para>
/// <para>
/// It writes nothing (ADR-0012). A mastery figure here is derived on read; there is no row to keep in
/// step with the answers.
/// </para>
/// </summary>
public sealed class PracticeMatrixService(CompanionDbContext db, TimeProvider? clock = null)
{
    private const string ActiveLifecycle = "active";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// The matrix for one learner's certificate. Empty when the certificate examines no skill whose
    /// language has topics — an honest empty, not a fabricated row.
    /// </summary>
    public async Task<IReadOnlyList<PracticeCellDto>> GetMatrixAsync(
        int userId, int certId, CancellationToken cancellationToken = default)
    {
        var modalities = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Select(s => s.Modality)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (modalities.Count == 0)
        {
            return [];
        }

        var topics = await db.Topics
            .AsNoTracking()
            .Where(t => modalities.Contains(t.Modality))
            .OrderBy(t => t.Modality)
            .ThenBy(t => t.Code)
            .Select(t => new { t.TopicId, t.Code, t.Name, t.Modality })
            .ToListAsync(cancellationToken);

        if (topics.Count == 0)
        {
            return [];
        }

        var topicIds = topics.Select(t => t.TopicId).ToList();

        // Each servable item once, with this learner's own answers against it. Newest version only, and
        // never a placement item — the same two rules the selector's candidate query states, so a cell
        // cannot look drillable here and empty there.
        var items = await db.Items
            .AsNoTracking()
            .Where(i => topicIds.Contains(i.TopicId)
                && i.LifecycleStatus == ActiveLifecycle
                && !i.PlacementEligible)
            .Select(i => new
            {
                i.ItemId,
                i.TopicId,
                i.CefrLevel,
                i.ItemType,
                TimesSeen = db.PracticeAnswers.Count(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId),
                LastWrongAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId && pa.IsCorrect == false)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
                LastCorrectAt = db.PracticeAnswers
                    .Where(pa => pa.ItemId == i.ItemId && pa.Session.UserId == userId && pa.IsCorrect == true)
                    .Max(pa => (DateTime?)pa.AnsweredAt),
                Answered = db.PracticeAnswers.Count(pa => pa.ItemId == i.ItemId
                    && pa.Session.UserId == userId && pa.IsCorrect != null),
                Correct = db.PracticeAnswers.Count(pa => pa.ItemId == i.ItemId
                    && pa.Session.UserId == userId && pa.IsCorrect == true),
            })
            .ToListAsync(cancellationToken);

        var now = _clock.GetUtcNow();
        var byCell = items
            .GroupBy(i => (i.TopicId, Level: i.CefrLevel.Code))
            .ToDictionary(g => g.Key, g => g.ToList());

        var cells = new List<PracticeCellDto>();
        foreach (var topic in topics)
        {
            foreach (var level in CefrLevel.AuthoredLevels)
            {
                if (!byCell.TryGetValue((topic.TopicId, level.Code), out var cellItems))
                {
                    // No stock at this level: there is no cell to colour. Inventing an "untested" plate
                    // for a cell that holds nothing would promise a drill the pool cannot serve.
                    continue;
                }

                var candidates = cellItems
                    .Select(i => new PoolCandidate(
                        i.ItemId,
                        0,
                        i.TopicId,
                        i.ItemType,
                        i.TimesSeen,
                        Utc(Max(i.LastWrongAt, i.LastCorrectAt)),
                        Utc(i.LastWrongAt),
                        Utc(i.LastCorrectAt)))
                    .ToList();

                var answered = cellItems.Sum(i => i.Answered);
                var correct = cellItems.Sum(i => i.Correct);
                var mastery = CellMastery.Estimate(answered, correct);

                // A cell rests when every item in it is cooling. The soonest one to return is when the
                // cell reopens; if anything is servable now, nothing reopens.
                var servable = candidates.Any(c => PoolRanking.IsServable(c, now));
                var reopensAt = servable ? null : PoolRanking.SoonestServableAt(candidates, now);

                cells.Add(new PracticeCellDto(
                    topic.TopicId,
                    topic.Code,
                    topic.Name,
                    EnumCodes.Parse<Modality>(topic.Modality).MapTo<ModalityDto>(),
                    level.Code,
                    ToDto(mastery),
                    IsPrioritised: false,
                    reopensAt,
                    IsResting: reopensAt is not null));
            }
        }

        return cells;
    }

    private static PracticeCellMasteryDto ToDto(MasteryState state) => state switch
    {
        MasteryState.Mastered => PracticeCellMasteryDto.Mastered,
        MasteryState.Reviewing => PracticeCellMasteryDto.Reviewing,
        MasteryState.Learning => PracticeCellMasteryDto.Learning,
        _ => PracticeCellMasteryDto.Untested,
    };

    private static DateTime? Max(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    /// <summary><c>PracticeAnswers.AnsweredAt</c> is a legacy <c>DATETIME2</c> written with
    /// <c>DateTime.UtcNow</c>, so its kind is UTC; saying so is not a conversion.</summary>
    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
}
