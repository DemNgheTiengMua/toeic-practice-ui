using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #243 / D3 / UI-34: the learner's weakest <c>(topic, level)</c> cell.
/// <para>
/// <b>It ranks; it does not compute.</b> Accuracy per cell arrives from <c>vw_UserTopicAccuracy</c>,
/// which is D3's single deterministic SQL for this figure. Dividing <c>Correct</c> by
/// <c>Answered</c> here - or recomputing a cell's mastery from the answers - would be a second
/// definition beside the view and beside #237's stored row, which is the defect the rebuild exists to
/// remove.
/// </para>
/// <para>
/// <b>The floor is PLC-68's <c>n_min</c>, reused rather than restated.</b> A cell holding three answers
/// and no correct ones is not a weakness yet: it is three answers. So a cell is <i>ranked</i> only at
/// 12 objective items or 3 productive tasks - the same constant the mastery model uses, read from
/// <see cref="MasteryEstimate.MinimumObservationsFor"/> so the two rules can never drift apart. The
/// cells below it are still <i>listed</i>, with their count, because hiding them would make the page
/// silent rather than honest: "2 of the 12 answers this needs".
/// </para>
/// <para>
/// <b>An empty cell is excluded, never ranked last.</b> With no evidence a cell would sort at 0% and
/// be named the learner's weakest topic, which is a statement about nothing that reads as a statement
/// about the learner - the exact fault SRS §2 item 3 records. Only view rows are considered at all, so
/// a topic no item exists for cannot appear.
/// </para>
/// <para>
/// No write path: this is a read (ADR-0012 names none of it as a derived fact).
/// </para>
/// </summary>
public sealed class WeaknessAnalysisService(
    CompanionDbContext db,
    ILearnerService learners) : IWeaknessAnalysisService
{
    public async Task<WeaknessAnalysisView> GetWeaknessAnalysisAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        var gradedExamCount = await db.ExamAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && a.CertId == activeCertId && a.Status == AttemptStatus.Graded.ToCode(), cancellationToken);

        var completedPracticeCount = await db.PracticeSessions
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == PracticeStatus.Finished.ToCode(), cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == activeCertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionIds = sections.Select(s => s.SectionId).ToHashSet();

        // Read numbers directly from the deterministic SQL view
        var sectionViewRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId && sectionIds.Contains(v.SectionId))
            .ToListAsync(cancellationToken);

        var sectionViewMap = sectionViewRows.ToDictionary(r => r.SectionId);

        var totalAnswered = 0;
        var totalCorrect = 0;

        var sectionAccList = new List<SectionAccuracy>();
        foreach (var s in sections)
        {
            sectionViewMap.TryGetValue(s.SectionId, out var row);
            var answered = row?.Answered ?? 0;
            var correct = row?.Correct ?? 0;
            totalAnswered += answered;
            totalCorrect += correct;

            var pct = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
            sectionAccList.Add(new SectionAccuracy(
                s.SectionId,
                s.Code,
                s.Name,
                answered,
                correct,
                pct,
                IsWeakest: false,
                IsStrongest: false));
        }

        var hasData = totalAnswered > 0;
        var overallPct = hasData ? (int)Math.Round((double)totalCorrect / totalAnswered * 100) : 0;

        // Determine weakest and strongest sections among those attempted
        var attemptedSections = sectionAccList.Where(s => s.Answered > 0).ToList();
        int? weakestSectionId = null;
        int? strongestSectionId = null;

        if (attemptedSections.Count > 0)
        {
            var minPct = attemptedSections.Min(s => s.AccuracyPercentage);
            var maxPct = attemptedSections.Max(s => s.AccuracyPercentage);

            // Weakest: lowest accuracy (break tie with highest questions answered, then order)
            weakestSectionId = attemptedSections
                .Where(s => s.AccuracyPercentage == minPct)
                .OrderByDescending(s => s.Answered)
                .First().SectionId;

            // Strongest: highest accuracy (only distinct when multiple attempted and max > min)
            if (attemptedSections.Count > 1 && maxPct > minPct)
            {
                strongestSectionId = attemptedSections
                    .Where(s => s.AccuracyPercentage == maxPct)
                    .OrderByDescending(s => s.Answered)
                    .First().SectionId;
            }
        }

        // Sort sections: weakest first, unattempted at the end
        var sortedSections = sectionAccList
            .Select(s => s with
            {
                IsWeakest = s.SectionId == weakestSectionId,
                IsStrongest = s.SectionId == strongestSectionId
            })
            .OrderBy(s => s.Answered > 0 ? 0 : 1)
            .ThenBy(s => s.AccuracyPercentage)
            .ThenByDescending(s => s.Answered)
            .ToList();

        var priorityPracticeSection = weakestSectionId.HasValue
            ? sortedSections.FirstOrDefault(s => s.SectionId == weakestSectionId.Value)
            : null;

        // Read question category accuracy directly from the SQL view
        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .ToListAsync(cancellationToken);

        var catIds = categories.Select(c => c.CategoryId).ToHashSet();

        var catViewRows = await db.vw_UserCategoryAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.CategoryId != null && catIds.Contains(v.CategoryId.Value))
            .ToListAsync(cancellationToken);

        var catViewMap = catViewRows.ToDictionary(r => r.CategoryId!.Value);

        var missedCategories = new List<CategoryAccuracy>();
        foreach (var c in categories)
        {
            if (catViewMap.TryGetValue(c.CategoryId, out var row) && (row.Answered ?? 0) > 0)
            {
                var answered = row.Answered ?? 0;
                var correct = row.Correct ?? 0;
                var pct = (int)Math.Round((double)correct / answered * 100);
                missedCategories.Add(new CategoryAccuracy(
                    c.CategoryId,
                    c.Code,
                    c.Name,
                    answered,
                    correct,
                    pct));
            }
        }

        // Sort categories weakest first
        var sortedMissedCategories = missedCategories
            .OrderBy(c => c.AccuracyPercentage)
            .ThenByDescending(c => c.Answered - c.Correct)
            .ThenByDescending(c => c.Answered)
            .ToList();

        var (topicCells, weakestTopic) = await BuildTopicAccuracyAsync(userId, activeCertId, cancellationToken);
        // Open recommendations for this learner and certificate
        var recos = await db.Recommendations
            .AsNoTracking()
            .Include(r => r.Skill)
            .Where(r => r.UserId == userId && r.CertId == activeCertId && !r.IsResolved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RecommendationItem(
                r.RecId,
                r.Text,
                EnumCodes.Parse<RecommendationSeverity>(r.Severity),
                r.TargetBandCode,
                r.Skill != null ? r.Skill.Name : null,
                r.CreatedAt,
                r.IsResolved))
            .ToListAsync(cancellationToken);

        return new WeaknessAnalysisView(
            cert,
            hasData || topicCells.Count > 0,
            gradedExamCount,
            completedPracticeCount,
            totalAnswered,
            totalCorrect,
            overallPct,
            sortedSections,
            sortedMissedCategories,
            recos,
            priorityPracticeSection,
            topicCells,
            weakestTopic);    }

    /// <summary>
    /// The topic axis: every cell the learner has evidence for, weakest first, plus the one cell that
    /// may be called the weakest.
    /// <para>
    /// <b>Certificate isolation without a <c>CertId</c> on a topic.</b> A topic belongs to the language
    /// (D-3), so the certificate is applied through the one axis it does declare: the modalities of its
    /// skills. A learner on a listening-and-reading certificate is never told a writing topic is weak,
    /// because that certificate does not examine writing. The lesson link is scoped by
    /// <c>CertId</c> as well, so the link cannot cross into another certificate's lesson.
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<TopicAccuracy> Cells, TopicAccuracy? Weakest)> BuildTopicAccuracyAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var examinedModalities = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Select(s => s.Modality)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (examinedModalities.Count == 0)
        {
            return ([], null);
        }

        // Only view rows: a cell exists here because an answer was graded in it. A topic with no items
        // has no answers, so it has no row and can never be reported - criterion 3 by construction.
        var rows = await db.vw_UserTopicAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return ([], null);
        }

        var topics = await db.Topics
            .AsNoTracking()
            .Where(t => examinedModalities.Contains(t.Modality))
            .Select(t => new { t.TopicId, t.Code, t.Name, t.Modality })
            .ToDictionaryAsync(t => t.TopicId, cancellationToken);

        if (topics.Count == 0)
        {
            return ([], null);
        }

        // One lesson per cell. The tie-break is #240's own (DisplayOrder, then LessonId), so "which of
        // them" is reproducible rather than the storage engine's choice.
        var lessons = (await db.Lessons
                .AsNoTracking()
                .Where(l => l.CertId == certId)
                .Select(l => new { l.LessonId, l.TopicId, l.CefrLevel, l.DisplayOrder })
                .ToListAsync(cancellationToken))
            .GroupBy(l => (l.TopicId, l.CefrLevel))
            .ToDictionary(
                g => g.Key,
                g => (int?)g.OrderBy(l => l.DisplayOrder).ThenBy(l => l.LessonId).First().LessonId);

        var cells = new List<TopicAccuracy>(rows.Count);
        foreach (var row in rows)
        {
            if (!topics.TryGetValue(row.TopicId, out var topic))
            {
                continue;
            }

            // A level this build cannot place is not a cell it can report; the row is skipped rather
            // than crashing a read path on a code from a newer schema.
            if (!CefrLevel.TryParseAuthored(row.CefrLevel, out var level))
            {
                continue;
            }

            var modality = EnumCodes.Parse<Modality>(topic.Modality);
            lessons.TryGetValue((row.TopicId, level), out var lessonId);

            cells.Add(new TopicAccuracy(
                row.TopicId,
                topic.Code,
                topic.Name,
                modality,
                level,
                row.Answered,
                row.Correct,
                row.AccuracyPercent,
                IsWeakest: false,
                MasteryEstimate.MinimumObservationsFor(modality),
                lessonId));
        }

        // PLC-68's n_min per modality: 12 objective items or 3 productive tasks. Read from the mastery
        // model's own function, so there is one floor in the codebase and not two.
        bool HasEnoughEvidence(TopicAccuracy c) => c.Answered >= c.ObservationFloor;

        var ranked = cells
            .Where(HasEnoughEvidence)
            .OrderBy(c => c.AccuracyPercent)
            .ThenByDescending(c => c.Answered - c.Correct)
            .ThenByDescending(c => c.Answered)
            .ThenBy(c => c.TopicId)
            .ThenBy(c => c.CefrLevel.Rank)
            .ToList();

        var weakest = ranked.FirstOrDefault();

        var ordered = cells
            // Rankable cells first, weakest first; the rest after, worst-evidenced last. A cell below
            // the floor is shown with its count so the page can say why it is not ranked.
            .OrderBy(c => HasEnoughEvidence(c) ? 0 : 1)
            .ThenBy(c => c.AccuracyPercent)
            .ThenByDescending(c => c.Answered - c.Correct)
            .ThenByDescending(c => c.Answered)
            .ThenBy(c => c.TopicId)
            .ThenBy(c => c.CefrLevel.Rank)
            .Select(c => c with { IsWeakest = weakest is not null && c.TopicId == weakest.TopicId && c.CefrLevel == weakest.CefrLevel })
            .ToList();

        return (ordered, ordered.FirstOrDefault(c => c.IsWeakest));
    }

    public async Task<bool> ResolveRecommendationAsync(int userId, int recId, CancellationToken cancellationToken = default)
    {
        var reco = await db.Recommendations
            .FirstOrDefaultAsync(r => r.RecId == recId && r.UserId == userId, cancellationToken);

        if (reco is null)
        {
            throw new RecommendationNotFoundException(recId);
        }

        reco.IsResolved = true;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
