using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #208: the rubric and grader calibration studio. Four routes, all keyed by (certificate, skill)
    /// because a rubric is scoped to one skill of one certificate.
    /// <para>
    /// <c>ladder</c> and <c>lock</c> are separate although both run the ladder, and the split is not
    /// cosmetic: running seven grader calls is expensive, and the author needs to see the plot before
    /// committing the rubric to it. <c>lock</c> re-runs the ladder rather than trusting a verdict the
    /// browser posted, because a browser is the one place a claim must never be read from.
    /// </para>
    /// </summary>
    private static void MapRubricStudio(RouteGroupBuilder admin)
    {
        admin.MapGet("/studio/rubric", async (
            int certId,
            int skillId,
            IRubricStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.GetAsync(certId, skillId, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/studio/rubric", async (
            SaveRubricRequest request,
            IRubricStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.SaveAsync(request, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/studio/rubric/ladder", async (
            RunRubricLadderRequest request,
            IRubricStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.RunLadderAsync(request, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/studio/rubric/lock", async (
            RunRubricLadderRequest request,
            IRubricStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.LockAsync(request, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
