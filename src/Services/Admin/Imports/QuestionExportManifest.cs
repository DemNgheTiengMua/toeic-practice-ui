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
/// The legacy columns are still the skeleton every paper has. Where the pool has assembled a
/// position - an <c>ExamItems</c> row exists for it - the pool's own content is written instead, since
/// that is what the learner sat and what a re-import has to reproduce.
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

        if (roster.Count > 0)
        {
            return await BuildFromRosterAsync(db, exam, roster, annotations, isZip, cancellationToken);
        }

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == exam.ExamId)
            .OrderBy(g => g.DisplayOrder)
            // Two collection levels (questions, then options): split, or EF warns 20504 and the
            // rows multiply (see the readiness audit).
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // A binding names a paper position, not a QuestionId, so the position is the rank within the
        // section - computed here in the same order AttemptEngine renders the paper, which is the
        // only thing the two shapes share.
        var positions = new Dictionary<int, (string SectionCode, int Position)>();
        var inSection = new Dictionary<int, int>();
        foreach (var group in groups.OrderBy(g => g.Section.DisplayOrder).ThenBy(g => g.DisplayOrder))
        {
            var position = inSection.GetValueOrDefault(group.SectionId);
            foreach (var question in group.QuestionGroups.OrderBy(q => q.DisplayOrder).ThenBy(q => q.QuestionId))
            {
                position++;
                positions[question.QuestionId] = (group.Section.Code, position);
            }

            inSection[group.SectionId] = position;
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
            Sections = groups
                .GroupBy(g => g.Section)
                .OrderBy(sg => sg.Key.DisplayOrder)
                .Select(sg => new ImportManifestSection
                {
                    SectionCode = sg.Key.Code,
                    Groups = sg.OrderBy(g => g.DisplayOrder)
                        .Select(g => BuildGroup(g, annotations, positions, isZip))
                        .ToList(),
                })
                .ToList(),
        };
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

    /// <summary>
    /// One group. A position the pool has assembled takes the pool's stimulus - the shared passage is
    /// written once for the group it belongs to, which is what makes an export re-import as one
    /// stimulus and several children rather than several copies of the text.
    /// </summary>
    private static ImportManifestGroup BuildGroup(
        QuestionGroup group,
        IReadOnlyDictionary<(string SectionCode, int Position), PoolQuestionAnnotation> annotations,
        IReadOnlyDictionary<int, (string SectionCode, int Position)> positions,
        bool isZip)
    {
        var questions = group.QuestionGroups.OrderBy(q => q.DisplayOrder).ToList();

        var poolStimulus = questions
            .Select(q => PoolFor(annotations, positions, q))
            .FirstOrDefault(a => a?.Stimulus is not null)?.Stimulus;

        var audioRel = string.IsNullOrWhiteSpace(group.AudioPath)
            ? null
            : $"media/{Path.GetFileName(group.AudioPath)}";
        var imageRel = string.IsNullOrWhiteSpace(group.ImagePath)
            ? null
            : $"media/{Path.GetFileName(group.ImagePath)}";

        return new ImportManifestGroup
        {
            UnitNumber = group.UnitNumber,
            Passage = poolStimulus?.Passage ?? group.Passage,
            AudioFile = isZip ? audioRel : null,
            AudioStartMs = group.AudioStartMs,
            AudioEndMs = group.AudioEndMs,
            ImageFile = isZip ? imageRel : null,
            ImageAlt = poolStimulus?.ImageAlt ?? group.ImageAlt,
            Transcript = poolStimulus?.Transcript ?? group.Transcript,
            PrepSeconds = poolStimulus?.PrepSeconds ?? group.PrepSeconds,
            ResponseSeconds = poolStimulus?.ResponseSeconds ?? group.ResponseSeconds,
            DisplayOrder = group.DisplayOrder,
            CefrLevel = poolStimulus?.CefrLevel,
            DomainContext = poolStimulus?.DomainContext,
            PromptCopy = poolStimulus?.PromptCopy,
            Questions = questions
                .Select(q => BuildQuestion(q, PoolFor(annotations, positions, q)))
                .ToList(),
        };
    }

    /// <summary>
    /// A child. The legacy row supplies everything the pool has no column for (word limits, timers,
    /// the rubric); the pool supplies everything scored (stem, options, key, explanation, evidence).
    /// </summary>
    private static ImportManifestQuestion BuildQuestion(Question q, PoolQuestionAnnotation? pool)
    {
        List<string>? acceptedAnswers = null;
        if (!string.IsNullOrWhiteSpace(q.AcceptedAnswersJson))
        {
            acceptedAnswers = AttemptEngine.ParseAcceptedAnswers(q.AcceptedAnswersJson);
        }

        List<string>? requiredKeywords = null;
        if (!string.IsNullOrWhiteSpace(q.RequiredKeywords))
        {
            requiredKeywords = AttemptEngine.ParseKeywords(q.RequiredKeywords);
        }

        if (pool is null)
        {
            return new ImportManifestQuestion
            {
                Stem = q.Stem,
                // #229 / D1: the level the legacy row carries, and no difficulty. The number is
                // dormant and NULL on anything written since #229, so emitting it would round-trip a
                // hole; emitting the level is what makes the file re-importable.
                CefrLevel = q.CefrLevel?.Code,
                QuestionType = q.QuestionType,
                CategoryCode = q.Category?.Code,
                Explanation = q.Explanation,
                Hint = q.Hint,
                SampleAnswerOrImageDesc = q.SampleAnswerOrImageDesc,
                ReferenceKind = q.ReferenceKind,
                PrepSeconds = q.PrepSeconds,
                ResponseSeconds = q.ResponseSeconds,
                MinWords = q.MinWords,
                MaxWords = q.MaxWords,
                DisplayOrder = q.DisplayOrder,
                MaxSelections = q.MaxSelections,
                AllowNumber = q.AllowNumber,
                AcceptedAnswers = acceptedAnswers,
                RequiredKeywords = requiredKeywords,
                Options = q.QuestionOptionQuestionNavigations.Count == 0
                    ? null
                    : q.QuestionOptionQuestionNavigations.OrderBy(o => o.Label).Select(o => new ImportManifestOption
                    {
                        Label = o.Label,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect,
                        Rationale = o.Rationale,
                    }).ToList(),
            };
        }

        return new ImportManifestQuestion
        {
            // #250: the pool's own content, so exporting what was imported and importing it again
            // produces the same item and version rather than the legacy skeleton it was also written
            // into. QuestionType is the RENDER format (the legacy column's meaning), so it comes from
            // the pool's RenderFormat: hardcoding "mcq" collapsed a multi_select to a single-answer
            // item on the way out, and its two correct options were then refused on the way back in.
            Stem = pool.Stem,
            CefrLevel = pool.CefrLevel,
            TopicCode = pool.TopicCode,
            ItemType = pool.ItemType,
            PlacementEligible = pool.PlacementEligible,
            QuestionType = pool.IsProductive ? "free_response" : pool.RenderFormat,
            CategoryCode = q.Category?.Code,
            Explanation = pool.Explanation,
            Hint = q.Hint,
            SampleAnswerOrImageDesc = pool.ReferenceText ?? q.SampleAnswerOrImageDesc,
            ReferenceKind = pool.ReferenceKind ?? q.ReferenceKind,
            PrepSeconds = q.PrepSeconds,
            ResponseSeconds = q.ResponseSeconds,
            MinWords = q.MinWords,
            MaxWords = q.MaxWords,
            DisplayOrder = q.DisplayOrder,
            MaxSelections = q.MaxSelections,
            AllowNumber = q.AllowNumber,
            AcceptedAnswers = acceptedAnswers,
            RequiredKeywords = requiredKeywords,
            EvidenceStart = pool.EvidenceStart,
            EvidenceEnd = pool.EvidenceEnd,
            EvidenceUnit = pool.EvidenceUnit,
            EvidenceQuote = pool.EvidenceQuote,
            Options = pool.IsProductive
                ? null
                : [
                    // #250: the key is a SET ('A,C' for multi_select), so equality would export a
                    // multi-answer item as having no correct option at all - and the roundtrip test
                    // caught exactly that: the re-import produced a batch with no key.
                    new ImportManifestOption { Label = "A", Text = pool.OptionA!, IsCorrect = BusinessObjects.KeyLabelSet.Contains(pool.KeyLabel, "A") },
                    new ImportManifestOption { Label = "B", Text = pool.OptionB!, IsCorrect = BusinessObjects.KeyLabelSet.Contains(pool.KeyLabel, "B") },
                    new ImportManifestOption { Label = "C", Text = pool.OptionC!, IsCorrect = BusinessObjects.KeyLabelSet.Contains(pool.KeyLabel, "C") },
                    new ImportManifestOption { Label = "D", Text = pool.OptionD!, IsCorrect = BusinessObjects.KeyLabelSet.Contains(pool.KeyLabel, "D") },
                ],
        };
    }

    /// <summary>The pool content at the position this legacy question occupies, or null when unbound.</summary>
    private static PoolQuestionAnnotation? PoolFor(
        IReadOnlyDictionary<(string SectionCode, int Position), PoolQuestionAnnotation> annotations,
        IReadOnlyDictionary<int, (string SectionCode, int Position)> positions,
        Question question) =>
        positions.TryGetValue(question.QuestionId, out var key)
        && annotations.TryGetValue(key, out var annotation)
            ? annotation
            : null;
}
