using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #204 / S-07: the productive-task studio. Three routes, none of which names an exam: a task is
    /// pool stock (CNT-01), and binding it to a paper is the deliberate second act on
    /// <c>POST /exams/{examId}/bindings</c>.
    /// <para>
    /// <c>analyse</c> is a POST although it writes nothing, for the same reason the stimulus studio's
    /// is: the draft it judges is a structure, not a query string.
    /// </para>
    /// </summary>
    private static void MapProductiveStudio(RouteGroupBuilder admin)
    {
        admin.MapGet("/studio/productive/options", async (
            string skill,
            IProductiveStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.GetOptionsAsync(skill, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/studio/productive/analyse", async (
            ProductiveTaskDraftRequest request,
            IProductiveStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await studio.AnalyseAsync(request, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/studio/productive", async (
            ProductiveTaskDraftRequest request,
            IProductiveStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var saved = await studio.SaveAsync(request, cancellationToken);
                return Results.Created($"/api/admin/studio/productive/{saved.StimulusId}", saved);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
