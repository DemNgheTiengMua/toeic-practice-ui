using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// The one writer for a criterion's seven-band grid and its three anchor responses.
/// <para>
/// #192 wrote this rule inside the certificate wizard; #208's studio needs the same rule, and a second
/// copy would be a second answer to "does a save merge or replace". The rule is replace: bands the
/// request omits are DELETED, because a merge lets a seven-band grid keep an eighth row from an earlier
/// save and the lock counts that as a defect. Two services calling one method is what stops the two
/// doors disagreeing about a grid a learner is graded against.
/// </para>
/// <para>
/// Both callers write through EF <c>SaveChanges</c>, never raw SQL, so
/// <see cref="DataAccess.StaffAccountStampInterceptor"/> stamps <c>LastWrittenBy</c> and #201's strict
/// catalog does not refuse the descriptor write.
/// </para>
/// </summary>
internal static class RubricGridWriter
{
    /// <summary>One descriptor cell: the band, what it looks like, and what must be true to reach it.</summary>
    internal readonly record struct Cell(byte Band, string Descriptor, string BinaryGates);

    /// <summary>One anchor: the band (2, 3 or 4 - the table refuses anything else) and the response.</summary>
    internal readonly record struct Anchor(byte Band, string ResponseText);

    internal static async Task ReplaceCellsAsync(
        CompanionDbContext db, int criterionId, IReadOnlyList<Cell> cells, CancellationToken cancellationToken)
    {
        var existing = await db.GradingCriterionDescriptors
            .Where(d => d.CriterionId == criterionId)
            .ToListAsync(cancellationToken);

        var incoming = cells
            .GroupBy(c => c.Band)
            .Select(g => g.First())
            .ToDictionary(c => c.Band);

        db.GradingCriterionDescriptors.RemoveRange(existing.Where(d => !incoming.ContainsKey(d.Band)));
        foreach (var cell in incoming.Values.OrderBy(c => c.Band))
        {
            var row = existing.FirstOrDefault(d => d.Band == cell.Band);
            if (row is null)
            {
                db.GradingCriterionDescriptors.Add(new GradingCriterionDescriptor
                {
                    CriterionId = criterionId,
                    Band = cell.Band,
                    Descriptor = cell.Descriptor.Trim(),
                    BinaryGates = cell.BinaryGates.Trim(),
                });
            }
            else
            {
                row.Descriptor = cell.Descriptor.Trim();
                row.BinaryGates = cell.BinaryGates.Trim();
            }
        }
    }

    internal static async Task ReplaceAnchorsAsync(
        CompanionDbContext db, int criterionId, IReadOnlyList<Anchor> anchors, CancellationToken cancellationToken)
    {
        var existing = await db.GradingCriterionAnchors
            .Where(a => a.CriterionId == criterionId)
            .ToListAsync(cancellationToken);

        var incoming = anchors
            .Where(a => !string.IsNullOrWhiteSpace(a.ResponseText))
            .GroupBy(a => a.Band)
            .Select(g => g.First())
            .ToDictionary(a => a.Band);

        db.GradingCriterionAnchors.RemoveRange(existing.Where(a => !incoming.ContainsKey(a.Band)));
        foreach (var anchor in incoming.Values.OrderBy(a => a.Band))
        {
            var row = existing.FirstOrDefault(a => a.Band == anchor.Band);
            if (row is null)
            {
                db.GradingCriterionAnchors.Add(new GradingCriterionAnchor
                {
                    CriterionId = criterionId,
                    Band = anchor.Band,
                    ResponseText = anchor.ResponseText.Trim(),
                });
            }
            else
            {
                row.ResponseText = anchor.ResponseText.Trim();
            }
        }
    }
}
