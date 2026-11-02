using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class SkillPracticeEndpoints
{
    /// <summary>The door list behind Writing and Speaking: the published free-response exams of the
    /// active certificate, with any attempt already open on each.</summary>
    public static void MapSkillPracticeEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/skill-practice").RequireAuthorization(Roles.Learner);

        learner.MapGet("/writing", async (HttpContext http, ISkillPracticeCatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(ToDto(await catalog.GetExamsAsync(CurrentUserId(http), ExamPurpose.WritingPractice, cancellationToken))));

        learner.MapGet("/speaking", async (HttpContext http, ISkillPracticeCatalogService catalog, CancellationToken cancellationToken) =>
            Results.Ok(ToDto(await catalog.GetExamsAsync(CurrentUserId(http), ExamPurpose.SpeakingPractice, cancellationToken))));
    }

    private static SkillPracticeExamListDto ToDto(BusinessObjects.SkillPracticeExamList view) => new(
        ToCertificateDto(view.Certificate),
        view.CreditBalance,
        view.Exams.Select(e => new SkillPracticeExamDto(
            e.ExamId, e.Name, e.Description, e.DurationMinutes, e.CreditCost,
            e.ActiveAttemptId, e.LatestGradedAttemptId, e.LatestGradedAt)).ToList());
}
