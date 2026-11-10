using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #265: the paper's positions, read for the admin editor.
    /// <para>
    /// <b>What used to be here, and why it is gone.</b> Create/update/delete question and
    /// create/update/delete group were exposed beside this read and called by no screen - the editor
    /// edits through its autosave and reorder routes, which write the pool. An endpoint whose backing
    /// table is being deleted must not answer at all, so those six routes are removed rather than left
    /// returning empty; a client that calls one gets a 404, which is the honest answer.
    /// </para>
    /// <para>
    /// The pool authoring door is the only authoring surface: author an item, then bind one of its
    /// versions to a position (<c>AdminEndpoints.Items</c>).
    /// </para>
    /// </summary>
    private static void MapQuestions(RouteGroupBuilder admin)
    {
        admin.MapGet("/exams/{examId:int}/questions", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var questions = await exams.GetExamQuestionsAsync(examId, cancellationToken);
                return Results.Ok(questions);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
