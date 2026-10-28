using BusinessObjects;
using Contracts;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WeaknessEndpoints
{
    private static WeaknessAnalysisDto ToDto(WeaknessAnalysisView view) => new(
        ToCertificateDto(view.Certificate),
        view.HasData,
        view.GradedExamCount,
        view.CompletedPracticeCount,
        view.TotalAnswered,
        view.TotalCorrect,
        view.OverallAccuracyPercentage,
        view.Sections.Select(s => new SectionAccuracyDto(
            s.SectionId, s.Code, s.Name, s.Answered, s.Correct, s.AccuracyPercentage, s.IsWeakest, s.IsStrongest)).ToList(),
        view.FrequentlyMissedCategories.Select(c => new CategoryAccuracyDto(
            c.CategoryId, c.Code, c.Name, c.Answered, c.Correct, c.AccuracyPercentage)).ToList(),
        view.Recommendations.Select(r => new RecommendationItemDto(
            r.RecId, r.Text, r.Severity.MapTo<RecommendationSeverityDto>(), r.TargetBandCode, r.SkillName, r.CreatedAt, r.IsResolved)).ToList(),
        view.PriorityPracticeSection is null ? null : new SectionAccuracyDto(
            view.PriorityPracticeSection.SectionId,
            view.PriorityPracticeSection.Code,
            view.PriorityPracticeSection.Name,
            view.PriorityPracticeSection.Answered,
            view.PriorityPracticeSection.Correct,
            view.PriorityPracticeSection.AccuracyPercentage,
            view.PriorityPracticeSection.IsWeakest,
            view.PriorityPracticeSection.IsStrongest));
}
