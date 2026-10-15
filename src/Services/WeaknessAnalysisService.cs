using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class RecommendationNotFoundException(int recId)
    : Exception($"Recommendation {recId} was not found.")
{
    public int RecId { get; } = recId;
}

public interface IWeaknessAnalysisService
{
    Task<WeaknessAnalysisView> GetWeaknessAnalysisAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> ResolveRecommendationAsync(int userId, int recId, CancellationToken cancellationToken = default);
}

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
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var gradedExamCount = await db.ExamAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && a.CertId == activeCertId && a.Status == AttemptStatus.Graded.ToCode(), cancellationToken);

        var completedPracticeCount = await db.PracticeSessions
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.Section.CertId == activeCertId && p.Status == PracticeStatus.Finished.ToCode(), cancellationToken);

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
            hasData,
            gradedExamCount,
            completedPracticeCount,
            totalAnswered,
            totalCorrect,
            overallPct,
            sortedSections,
            sortedMissedCategories,
            recos,
            priorityPracticeSection);
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
