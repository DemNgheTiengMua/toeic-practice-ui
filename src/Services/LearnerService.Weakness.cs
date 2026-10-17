using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private const int WeaknessRadarLimit = 5;

    private async Task<IReadOnlyList<WeaknessStrip>> BuildWeaknessStripsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SectionId)
            .ToListAsync(cancellationToken);

        if (sections.Count == 0)
        {
            return [];
        }

        var sectionIds = sections.Select(s => s.SectionId).ToHashSet();

        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .OrderBy(c => c.CategoryId)
            .ToListAsync(cancellationToken);

        var categoryIds = categories.Select(c => c.CategoryId).ToHashSet();

        var categoryViewRows = categoryIds.Count > 0
            ? await db.vw_UserCategoryAccuracies
                .AsNoTracking()
                .Where(v => v.UserId == userId && v.CategoryId != null && categoryIds.Contains(v.CategoryId.Value) && v.Answered > 0)
                .ToListAsync(cancellationToken)
            : [];

        var sectionViewRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId && sectionIds.Contains(v.SectionId) && v.Answered > 0)
            .ToListAsync(cancellationToken);

        if (categoryViewRows.Count == 0 && sectionViewRows.Count == 0)
        {
            return sections
                .Take(WeaknessRadarLimit)
                .Select(s => new WeaknessStrip(
                    Scope: "section",
                    Id: s.SectionId,
                    Code: s.Code,
                    Name: s.Name,
                    SectionName: s.Name,
                    Answered: 0,
                    Correct: 0,
                    AccuracyPercent: 0,
                    Tier: "UNTESTED",
                    ActionLabel: "Practice",
                    ActionUrl: "/practice"))
                .ToList();
        }

        var candidates = new List<(WeaknessStrip Strip, int DisplayOrder)>();
        var coveredSectionIds = new HashSet<int>();

        if (categoryViewRows.Count > 0)
        {
            var answeredCatIds = categoryViewRows.Select(r => r.CategoryId!.Value).ToHashSet();
            var categoryMap = categories.ToDictionary(c => c.CategoryId);

            var categorySectionRows = await db.Questions
                .AsNoTracking()
                .Where(q => q.CategoryId != null && answeredCatIds.Contains(q.CategoryId.Value) && sectionIds.Contains(q.SectionId))
                .Select(q => new
                {
                    CategoryId = q.CategoryId!.Value,
                    q.SectionId,
                    SectionName = q.Section.Name,
                    SectionDisplayOrder = q.Section.DisplayOrder,
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            var categoryToSection = categorySectionRows
                .GroupBy(x => x.CategoryId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SectionDisplayOrder).ThenBy(x => x.SectionId).First());

            foreach (var cs in categorySectionRows)
            {
                coveredSectionIds.Add(cs.SectionId);
            }

            var fallbackSection = sections[0];

            foreach (var row in categoryViewRows)
            {
                var catId = row.CategoryId!.Value;
                if (!categoryMap.TryGetValue(catId, out var cat))
                {
                    continue;
                }

                var answered = row.Answered ?? 0;
                if (answered <= 0)
                {
                    continue;
                }

                var correct = row.Correct ?? 0;
                var accuracyPercent = (int)Math.Round((double)correct / answered * 100);
                var tier = ClassifyWeaknessTier(accuracyPercent);

                categoryToSection.TryGetValue(catId, out var secInfo);
                var sectionName = secInfo?.SectionName ?? fallbackSection.Name;
                var displayOrder = secInfo?.SectionDisplayOrder ?? fallbackSection.DisplayOrder;

                candidates.Add((
                    new WeaknessStrip(
                        Scope: "category",
                        Id: cat.CategoryId,
                        Code: cat.Code,
                        Name: cat.Name,
                        SectionName: sectionName,
                        Answered: answered,
                        Correct: correct,
                        AccuracyPercent: accuracyPercent,
                        Tier: tier,
                        ActionLabel: "Practice",
                        ActionUrl: "/practice"),
                    displayOrder));
            }
        }

        var sectionMap = sections.ToDictionary(s => s.SectionId);
        foreach (var row in sectionViewRows)
        {
            if (coveredSectionIds.Contains(row.SectionId))
            {
                continue;
            }

            if (!sectionMap.TryGetValue(row.SectionId, out var sec))
            {
                continue;
            }

            var answered = row.Answered ?? 0;
            if (answered <= 0)
            {
                continue;
            }

            var correct = row.Correct ?? 0;
            var accuracyPercent = (int)Math.Round((double)correct / answered * 100);
            var tier = ClassifyWeaknessTier(accuracyPercent);

            candidates.Add((
                new WeaknessStrip(
                    Scope: "section",
                    Id: sec.SectionId,
                    Code: sec.Code,
                    Name: sec.Name,
                    SectionName: sec.Name,
                    Answered: answered,
                    Correct: correct,
                    AccuracyPercent: accuracyPercent,
                    Tier: tier,
                    ActionLabel: "Practice",
                    ActionUrl: "/practice"),
                sec.DisplayOrder));
        }

        return candidates
            .OrderBy(c => c.Strip.AccuracyPercent)
            .ThenByDescending(c => c.Strip.Answered)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Strip.Id)
            .Take(WeaknessRadarLimit)
            .Select(c => c.Strip)
            .ToList();
    }

    private static string ClassifyWeaknessTier(int accuracyPercent) => accuracyPercent switch
    {
        < 50 => "CRITICAL",
        <= 64 => "BORDER",
        <= 79 => "STABLE",
        _ => "MASTERY",
    };
}

