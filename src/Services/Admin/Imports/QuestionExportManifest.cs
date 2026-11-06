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

        var annotations = await PoolImport.LoadPaperAnnotationsAsync(db, exam.ExamId, cancellationToken);

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

        var isZip = string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase);

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
            // The pool's own content, so exporting what was imported and importing it again produces
            // the same item and version rather than the legacy skeleton it was also written into.
            Stem = pool.Stem,
            CefrLevel = pool.CefrLevel,
            TopicCode = pool.TopicCode,
            ItemType = pool.ItemType,
            PlacementEligible = pool.PlacementEligible,
            // The pool has no seven-way type: a productive version is the legacy free_response, and
            // an objective one is a four-option MCQ by construction.
            QuestionType = pool.IsProductive ? "free_response" : "mcq",
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
                    new ImportManifestOption { Label = "A", Text = pool.OptionA!, IsCorrect = pool.KeyLabel == "A" },
                    new ImportManifestOption { Label = "B", Text = pool.OptionB!, IsCorrect = pool.KeyLabel == "B" },
                    new ImportManifestOption { Label = "C", Text = pool.OptionC!, IsCorrect = pool.KeyLabel == "C" },
                    new ImportManifestOption { Label = "D", Text = pool.OptionD!, IsCorrect = pool.KeyLabel == "D" },
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
