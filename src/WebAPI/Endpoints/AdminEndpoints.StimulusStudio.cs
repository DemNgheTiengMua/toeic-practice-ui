using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #203 / S-05: the stimulus-and-children studio. Three routes, none of which names an exam: a
    /// bundle is pool stock (CNT-01), and binding it to a paper is the deliberate second act on
    /// <c>POST /exams/{examId}/bindings</c>.
    /// <para>
    /// <c>analyse</c> is a POST although it writes nothing. The draft it judges is a structure, not a
    /// query string, and a GET with a bundle in its body is a request no proxy or cache can be trusted
    /// with.
    /// </para>
    /// </summary>
    private static void MapStimulusStudio(RouteGroupBuilder admin)
    {
        admin.MapGet("/studio/stimulus/options", async (
            string skill,
            IStimulusStudioService studio,
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

        admin.MapPost("/studio/stimulus/analyse", async (
            StimulusDraftRequest request,
            IStimulusStudioService studio,
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

        admin.MapPost("/studio/stimulus", async (
            StimulusDraftRequest request,
            IStimulusStudioService studio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var saved = await studio.SaveAsync(request, cancellationToken);
                return Results.Created($"/api/admin/studio/stimulus/{saved.StimulusId}", saved);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
