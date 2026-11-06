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
    public async Task<AdminCoverageMatrixDto> GetMatrixAsync(CancellationToken cancellationToken = default)
    {
        var cells = await db.vw_CoverageMatrices
            .AsNoTracking()
            .OrderBy(c => c.Modality)
            .ThenBy(c => c.TopicCode)
            .ThenBy(c => c.CefrLevel)
            .Select(c => new AdminCoverageCellDto(
                c.TopicId,
                c.Modality,
                c.TopicCode,
                c.TopicName,
                c.CefrLevel,
                c.TargetItemCount,
                c.ActiveItemCount,
                c.PlacementEligibleItemCount,
                c.Deficit))
            .ToListAsync(cancellationToken);

        return new AdminCoverageMatrixDto(cells);
    }
}
