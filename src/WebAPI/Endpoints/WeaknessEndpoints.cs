using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class WeaknessEndpoints
{
    public static void MapWeaknessEndpoints(this IEndpointRouteBuilder app)
    {
        var weakness = app.MapGroup("/api/learner")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Learner));

        weakness.MapGet("/weakness-analysis", async (
            HttpContext http,
            IWeaknessAnalysisService weaknessService,
            CancellationToken cancellationToken) =>
        {
            var view = await weaknessService.GetWeaknessAnalysisAsync(CurrentUserId(http), cancellationToken);
            var dto = new WeaknessAnalysisDto(
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
            return Results.Ok(dto);
        });

        weakness.MapPost("/recommendations/{recId:int}/resolve", async (
            int recId,
            HttpContext http,
            IWeaknessAnalysisService weaknessService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await weaknessService.ResolveRecommendationAsync(CurrentUserId(http), recId, cancellationToken);
                return Results.Ok(new ResolveRecommendationResultDto(recId, true));
            }
            catch (RecommendationNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Recommendation not found", ApiError.Codes.RecommendationNotFound, ex.Message);
            }
        });
    }
}
