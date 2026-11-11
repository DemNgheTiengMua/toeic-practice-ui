using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #189: the one manifest builder both exports use.
/// <para>
/// Before this, <see cref="AdminExamService"/> and <see cref="QuestionImportService"/> each built the
/// manifest from the same query, and they had already drifted: only one wrote
/// <c>sampleAnswerOrImageDesc</c> and <c>referenceKind</c>, so #152's spoken reference survived one
/// door and vanished through the other. Two writers of one file is the drift this repo keeps
/// finding, so the shape is stated once.
/// </para>
/// <para>
/// #269: a paper's content is its roster, and there is no second source. The legacy columns the
/// fallback used are dropped with their tables, so the roster is read unconditionally rather than
/// preferred - a branch that could no longer match was a branch nobody could reach.
/// </para>
/// </summary>
internal static class QuestionExportManifest
{
    internal static async Task<ImportManifest> BuildAsync(
        CompanionDbContext db,
        Exam exam,
        string? format,
        CancellationToken cancellationToken)
    {
        var annotations = await PoolImport.LoadPaperAnnotationsAsync(db, exam.ExamId, cancellationToken);
        var isZip = string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase);

        // #269: a paper's content is its roster. The export reads it through the same projection the
        // sitting uses, so a re-import reproduces what a learner actually sat - including the pinned
        // version's stem and options rather than whatever an item's newest version says today.
        var roster = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(cancellationToken);

        return await BuildFromRosterAsync(db, exam, roster, annotations, isZip, cancellationToken);
    }

    /// <summary>
    /// #269: the manifest for a paper whose content is the roster.
    /// <para>
    /// The group is the stimulus, and a position with no stimulus is its own group of one - the same
    /// reading the Studio makes, so an exported pack re-imports as one stimulus and its children rather
    /// than as several copies of the same passage.
    /// </para>
    /// </summary>
    private static async Task<ImportManifest> BuildFromRosterAsync(
        CompanionDbContext db,
        Exam exam,
        List<ExamItem> roster,
        IReadOnlyDictionary<(string SectionCode, int Position), PoolQuestionAnnotation> annotations,
        bool isZip,
        CancellationToken cancellationToken)
    {
        // The export reads each position's own row, not the sitting projection: the projection chooses
        // ONE source for a paper, and an export must describe the roster even on a database that still
        // holds legacy rows beside it (the transition). A projection-keyed lookup went empty there and
        // wrote stems of "" - which the importer then refused, correctly.
        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => roster.Select(i => i.ItemVersionId).Contains(v.ItemVersionId))
            .ToDictionaryAsync(v => v.ItemVersionId, cancellationToken);

        var items = await db.Items.AsNoTracking()
            .Where(i => roster.Select(r => r.ItemId).Contains(i.ItemId))
            .ToDictionaryAsync(i => i.ItemId, cancellationToken);

        var stimulusIds = items.Values
            .Where(i => i.StimulusId.HasValue)
            .Select(i => i.StimulusId!.Value)
            .Distinct()
            .ToList();

        var stimuli = stimulusIds.Count == 0
            ? new Dictionary<int, Stimulus>()
            : (await db.Stimuli.AsNoTracking()
                .Where(s => stimulusIds.Contains(s.StimulusId))
                .ToListAsync(cancellationToken))
                .ToDictionary(s => s.StimulusId);

        var sectionIds = roster.Select(i => i.SectionId).Distinct().ToList();
        var sections = await db.Sections.AsNoTracking()
            .Where(s => sectionIds.Contains(s.SectionId))
            .OrderBy(s => s.DisplayOrder)
            .ToDictionaryAsync(s => s.SectionId, cancellationToken);

        var sectionsOut = new List<ImportManifestSection>();
        foreach (var sectionId in sectionIds.OrderBy(id => sections[id].DisplayOrder))
        {
            var section = sections[sectionId];
            var rows = roster.Where(i => i.SectionId == sectionId).OrderBy(i => i.DisplayOrder).ToList();

            var groupsOut = new List<ImportManifestGroup>();
            var position = 0;
            foreach (var bundle in rows.GroupBy(i =>
                items.GetValueOrDefault(i.ItemId)?.StimulusId ?? -i.ExamItemId))
            {
                var ordered = bundle.OrderBy(i => i.DisplayOrder).ToList();
                var questions = new List<ImportManifestQuestion>();

                foreach (var row in ordered)
                {
                    position++;
                    var version = versions[row.ItemVersionId];
                    var item = items[row.ItemId];
                    var annotation = annotations.GetValueOrDefault((section.Code, position));

                    questions.Add(new ImportManifestQuestion
                    {
                        Stem = version.Stem,
                        QuestionType = item.RenderFormat,
                        CefrLevel = item.CefrLevel.Code,
                        TopicCode = annotation?.TopicCode,
                        ItemType = annotation?.ItemType,
                        Explanation = version.Explanation,
                        Hint = version.Hint,
                        MinWords = version.MinWords,
                        MaxWords = version.MaxWords,
                        PrepSeconds = version.PrepSeconds,
                        ResponseSeconds = version.ResponseSeconds,
                        SampleAnswerOrImageDesc = version.ReferenceText,
                        ReferenceKind = version.ReferenceKind,
                        MaxSelections = version.MaxSelections,
                        AllowNumber = version.AllowNumber,
                        AcceptedAnswersJson = version.AcceptedAnswersJson,
                        RequiredKeywords = version.RequiredKeywords is { Length: > 0 } kw
                            ? kw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : null,
                        EvidenceStart = version.EvidenceStart,
                        EvidenceEnd = version.EvidenceEnd,
                        EvidenceUnit = version.EvidenceUnit,
                        EvidenceQuote = version.EvidenceQuote,
                        PlacementEligible = item.PlacementEligible,
                        Options = AttemptEngine.IsProductive(version)
                            ? []
                            : AttemptEngine.OptionTexts(version)
                                .Select((text, index) =>
                                {
                                    var label = AttemptEngine.OptionLabel(index);
                                    return new ImportManifestOption
                                    {
                                        Label = label,
                                        Text = text ?? string.Empty,
                                        IsCorrect = AttemptEngine.IsKeyLabel(version, label),
                                        Rationale = RationaleForVersion(version, label),
                                    };
                                })
                                .ToList(),
                    });
                }

                var first = ordered[0];
                var firstStimulus = items.GetValueOrDefault(first.ItemId)?.StimulusId is int sid
                    ? stimuli.GetValueOrDefault(sid)
                    : null;

                groupsOut.Add(new ImportManifestGroup
                {
                    UnitNumber = first.UnitNumber,
                    Passage = firstStimulus?.Passage,
                    Transcript = firstStimulus?.Transcript,
                    AudioFile = string.IsNullOrWhiteSpace(firstStimulus?.AudioPath)
                        ? null
                        : $"media/{Path.GetFileName(firstStimulus.AudioPath)}",
                    AudioStartMs = firstStimulus?.AudioStartMs,
                    AudioEndMs = firstStimulus?.AudioEndMs,
                    ImageFile = string.IsNullOrWhiteSpace(firstStimulus?.ImagePath)
                        ? null
                        : $"media/{Path.GetFileName(firstStimulus.ImagePath)}",
                    ImageAlt = firstStimulus?.ImageAlt,
                    CefrLevel = firstStimulus?.CefrLevel.Code,
                    DomainContext = firstStimulus?.DomainContext,
                    Questions = questions,
                });
            }

            sectionsOut.Add(new ImportManifestSection
            {
                SectionCode = section.Code,
                Groups = groupsOut,
            });
        }

        return new ImportManifest
        {
            Version = "1.0",
            Exam = new ImportManifestExam
            {
                Title = exam.Name,
                Purpose = exam.Purpose,
                DeliveryMode = exam.DeliveryMode,
                DurationMinutes = exam.DurationMinutes,
                SourceName = exam.SourceName,
                Description = exam.Description,
                QuestionCount = exam.QuestionCount,
            },
            Sections = sectionsOut,
        };
    }

    /// <summary>The rationale for one option of a version, which stores one per column.</summary>
    private static string? RationaleForVersion(ItemVersion version, string label) => label switch
    {
        "A" => version.RationaleA,
        "B" => version.RationaleB,
        "C" => version.RationaleC,
        "D" => version.RationaleD,
        _ => null,
    };
}
