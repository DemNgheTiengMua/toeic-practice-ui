using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    /// <summary>
    /// #188: the pool authoring routes. They sit on <c>pool.content.author</c> with the rest of the
    /// authoring surface, and none of them names an exam: a paper reaches an item through
    /// <c>POST /exams/{examId}/bindings</c>, which is the deliberate second act.
    /// </summary>
    private static void MapItems(RouteGroupBuilder admin)
    {
        admin.MapPost("/items", async (
            CreatePoolItemRequest request,
            IAdminItemService items,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await items.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/admin/items/{created.ItemId}", created);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/items/{itemId:int}", async (
            int itemId,
            UpdatePoolItemRequest request,
            IAdminItemService items,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await items.UpdateAsync(itemId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/bindings", async (
            int examId,
            BindPoolItemRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var binding = await exams.BindItemAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/exams/{examId}/bindings/{binding.ExamItemId}", binding);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
