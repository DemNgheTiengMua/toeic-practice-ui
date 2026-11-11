using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #271: the learner's <c>(topic, level)</c> accuracy cells for ONE certificate, read from
/// <c>vw_UserTopicAccuracy</c>.
/// <para>
/// <b>It reads; it does not compute.</b> <see cref="Cell.AccuracyPercent"/> is the view's arithmetic
/// (D3: "accuracy is computed ... by a view, not by application code"), so no caller divides
/// <see cref="Cell.Correct"/> by <see cref="Cell.Answered"/> again. This exists because five screens
/// report the same figure now - the weakness page and the four AI insight payloads - and five copies of
/// the scoping rule is five chances for one of them to name a cell the others do not.
/// </para>
/// <para>
/// <b>Certificate isolation without a <c>CertId</c> on a topic.</b> A topic belongs to the language
/// (D-3), so the certificate is applied through the one axis it does declare: the modalities of its
/// skills. A listening-and-reading learner is never told a writing cell is weak, because that
/// certificate does not examine writing.
/// </para>
/// <para>
/// <b>A legacy answer has no cell and contributes no row here.</b> <c>Questions</c> carries no
/// <c>TopicId</c>, so a legacy answer is invisible to the view. That is correct rather than an
/// omission: the topic vocabulary is a closed set of twenty and no mapping from a retired category
/// exists. A caller must therefore treat an empty list as "no cell yet", never as 0%.
/// </para>
/// </summary>
internal static class TopicAccuracyReader
{
    /// <summary>One cell, already parsed into the product's own axes.</summary>
    internal sealed record Cell(
        int TopicId,
        string TopicCode,
        string TopicName,
        Modality Modality,
        CefrLevel CefrLevel,
        int Answered,
        int Correct,
        int AccuracyPercent);

    /// <summary>
    /// Every cell this certificate's learner has evidence for. A level this build cannot parse is
    /// skipped rather than thrown on: a read path must degrade to "no cell", not fail a page, when it
    /// meets a code from a newer schema.
    /// </summary>
    internal static async Task<List<Cell>> ReadAsync(
        CompanionDbContext db, int userId, int certId, CancellationToken cancellationToken = default)
    {
        var examinedModalities = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Select(s => s.Modality)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (examinedModalities.Count == 0)
        {
            return [];
        }

        // Only view rows: a cell exists here because an answer was graded in it, so a topic with no
        // items has no row and can never be reported.
        var rows = await db.vw_UserTopicAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var topics = await db.Topics
            .AsNoTracking()
            .Where(t => examinedModalities.Contains(t.Modality))
            .Select(t => new { t.TopicId, t.Code, t.Name, t.Modality })
            .ToDictionaryAsync(t => t.TopicId, cancellationToken);

        var cells = new List<Cell>(rows.Count);
        foreach (var row in rows)
        {
            if (!topics.TryGetValue(row.TopicId, out var topic)
                || !CefrLevel.TryParseAuthored(row.CefrLevel, out var level))
            {
                continue;
            }

            cells.Add(new Cell(
                row.TopicId,
                topic.Code,
                topic.Name,
                EnumCodes.Parse<Modality>(topic.Modality),
                level,
                row.Answered,
                row.Correct,
                row.AccuracyPercent));
        }

        return cells;
    }
}
