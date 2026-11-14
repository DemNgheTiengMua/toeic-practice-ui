using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #202 / S-01: the coverage matrix, read straight out of <c>vw_CoverageMatrix</c>.
/// <para>
/// Every number the screen prints — target, active stock, placement-eligible stock, deficit — is a
/// column of that view. Nothing here counts, subtracts or groups, because a count computed here would
/// be a second definition of "coverage" and the two would eventually disagree. The view is also where
/// the empty cells come from: it produces all 120 rows, so a cell holding nothing arrives as a row with
/// a deficit rather than as an absence this method would have to invent a fix for.
/// </para>
/// </summary>
public sealed class AdminCoverageService(CompanionDbContext db) : IAdminCoverageService
{
    private static readonly string[] CefrLevels = ["A1", "A2", "B1", "B2", "C1", "C2"];

    private static int TargetFor(string modality, string cefrLevel) =>
        (modality is "speaking" or "writing")
            ? (cefrLevel is "A1" or "C2" ? 4 : 8)
            : (cefrLevel is "A1" or "C2" ? 15 : 30);

    public async Task<AdminCoverageMatrixDto> GetMatrixAsync(CancellationToken cancellationToken = default)
    {
        var topics = await db.Topics
            .AsNoTracking()
            .OrderBy(t => t.Modality)
            .ThenBy(t => t.Code)
            .Select(t => new { t.TopicId, t.Modality, t.Code, t.Name })
            .ToListAsync(cancellationToken);

        var counts = await db.Items
            .AsNoTracking()
            .Where(i => i.LifecycleStatus == "active")
            .GroupBy(i => new { i.TopicId, i.CefrLevel })
            .Select(g => new
            {
                g.Key.TopicId,
                CefrLevel = g.Key.CefrLevel.Code,
                Active = g.Count(),
                PlacementEligible = g.Count(i => i.PlacementEligible)
            })
            .ToListAsync(cancellationToken);

        var byCell = counts.ToDictionary(c => (c.TopicId, c.CefrLevel));
        var cells = new List<AdminCoverageCellDto>(topics.Count * CefrLevels.Length);

        foreach (var t in topics)
        {
            foreach (var level in CefrLevels)
            {
                var target = TargetFor(t.Modality, level);
                byCell.TryGetValue((t.TopicId, level), out var stat);
                var active = stat?.Active ?? 0;
                var eligible = stat?.PlacementEligible ?? 0;
                var deficit = Math.Max(0, target - active);
                cells.Add(new AdminCoverageCellDto(
                    t.TopicId, t.Modality, t.Code, t.Name, level, target, active, eligible, deficit));
            }
        }

        return new AdminCoverageMatrixDto(cells);
    }
}
