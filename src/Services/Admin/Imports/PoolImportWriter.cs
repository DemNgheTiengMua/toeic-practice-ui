using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #189: one writer for "these rows become pool stock, and this paper binds them".
/// <para>
/// Both import routes funnel through here so the two doorways cannot disagree about how a shared
/// passage is shared, what tier an imported item lands in, or which position a binding names. The
/// legacy shape is written by each importer; this adds the pool half, which is why an import that the
/// pool cannot hold still succeeds and warns instead of refusing the file.
/// </para>
/// </summary>
internal static class PoolImportWriter
{
    /// <summary>
    /// One legacy question that already exists, plus what the pool should hold for it.
    /// <see cref="StimulusKey"/> is the group: two children of one group share a passage by sharing a
    /// key, and a group that carries nothing gets no stimulus at all.
    /// </summary>
    internal sealed record Request(
        int QuestionId,
        string StimulusKey,
        PoolContent Content,
        string Stem,
        string? Explanation,
        string? ReferenceText,
        string? ReferenceKind,
        PoolImport.StimulusInput Stimulus);

    internal sealed record Outcome(
        int ItemsWritten,
        int StimuliWritten,
        int Bound,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Writes stimuli, items, versions and - when the paper may hold them - bindings. Two saves, in
    /// this order, because the second needs the ids the first generated: an item is never saved
    /// without its version, and a binding never names an id that does not exist yet.
    /// <para>
    /// A binding's position is the rank the paper will render the question at, computed from the rows
    /// that now exist rather than from the file's own numbering. That is the only join a binding has:
    /// <c>ExamItems</c> names no <c>QuestionId</c>, and <c>UQ_ExamItems_Exam_Section_Order</c> makes
    /// (section, position) the identity of a paper slot.
    /// </para>
    /// </summary>
    internal static async Task<Outcome> WriteAsync(
        CompanionDbContext db,
        Exam exam,
        int sectionId,
        string modality,
        IReadOnlyList<Request> requests,
        string? batchTag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        if (requests.Count == 0)
        {
            return new Outcome(0, 0, 0, notes);
        }

        // One stimulus per group, not one per child: that is the whole difference between a shared
        // passage and three copies of it. Insertion order is request order, so the first child of a
        // group decides the shared row.
        var stimuliByKey = new Dictionary<string, Stimulus>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (stimuliByKey.ContainsKey(request.StimulusKey) || !request.Stimulus.CarriesContent)
            {
                continue;
            }

            var level = CefrLevel.TryParseAuthored(request.Stimulus.LevelCode, out var parsed)
                ? parsed
                : request.Content.Level;
            stimuliByKey[request.StimulusKey] = PoolImport.BuildStimulus(request.Stimulus, level, 0);
        }

        db.Stimuli.AddRange(stimuliByKey.Values);

        var written = new List<(Request Request, Item Item, ItemVersion Version)>(requests.Count);
        foreach (var request in requests)
        {
            var stimulus = stimuliByKey.GetValueOrDefault(request.StimulusKey);
            var (item, version) = PoolImport.BuildItem(
                request.Content.TopicId,
                request.Content,
                request.Stem,
                request.Explanation,
                request.ReferenceText,
                request.ReferenceKind,
                stimulus,
                batchTag,
                now);

            db.Items.Add(item);
            db.ItemVersions.Add(version);
            written.Add((request, item, version));
        }

        await db.SaveChangesAsync(cancellationToken);

        // INV-6: a production paper may not bind demo stock, and an imported item is demo. Writing
        // the stock is right either way - it is the binding this paper may not hold - so the items
        // stay and the batch skips its bindings with one stated reason rather than half of them.
        if (exam.ContentTier != "demo")
        {
            return new Outcome(
                written.Count,
                stimuliByKey.Count,
                0,
                [$"This paper draws {exam.ContentTier} stock and an imported item is demo (INV-6): {written.Count} items were written to the pool but not bound to this paper."]);
        }

        var positions = await PaperPositionsAsync(db, exam.ExamId, sectionId, cancellationToken);

        var bindings = written
            .Where(entry => positions.ContainsKey(entry.Request.QuestionId))
            .Select(entry => new ExamItem
            {
                ExamId = exam.ExamId,
                SectionId = sectionId,
                CertId = exam.CertId,
                ItemId = entry.Item.ItemId,
                ItemVersionId = entry.Version.ItemVersionId,
                Modality = modality,
                DisplayOrder = positions[entry.Request.QuestionId],
                UnitNumber = 1,
            })
            .ToList();

        // #227: no structure-lock call here, and that is a verified decision rather than an omission.
        // This writer has exactly two callers: `ExamQuickIngestService.CommitStagedAsync` calls the
        // lock at its own line 55 before it reaches this writer, and `QuestionImportService.CommitAsync`
        // only ever writes a paper it created one statement earlier (`db.Exams.Add`, Persistence.cs:224),
        // which by definition has no attempts. A lock call here would be a third copy of a rule both
        // callers already satisfy, and it would refuse the import path that is correct today.
        db.ExamItems.AddRange(bindings);
        await db.SaveChangesAsync(cancellationToken);

        return new Outcome(written.Count, stimuliByKey.Count, bindings.Count, notes);
    }

    /// <summary>
    /// Each question's 1-based rank within its section, in the order the paper renders them. That
    /// order is (group display order, question display order) - the same ordering
    /// <c>AttemptEngine</c> places bindings by, and the reason an unbound paper still renders.
    /// </summary>
    private static async Task<Dictionary<int, int>> PaperPositionsAsync(
        CompanionDbContext db,
        int examId,
        int sectionId,
        CancellationToken cancellationToken)
    {
        var ordered = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId && q.SectionId == sectionId)
            .OrderBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ThenBy(q => q.QuestionId)
            .Select(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        return ordered
            .Select((questionId, index) => (questionId, position: index + 1))
            .ToDictionary(row => row.questionId, row => row.position);
    }
}
