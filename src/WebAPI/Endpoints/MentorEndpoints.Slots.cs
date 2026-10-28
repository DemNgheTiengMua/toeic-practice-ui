using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    private static void MapSlots(RouteGroupBuilder admin)
    {

        admin.MapGet("/{mentorId:int}/slots", async (int mentorId, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var slots = await mentorService.GetAdminMentorSlotsAsync(mentorId, cancellationToken);
                return Results.Ok(slots);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapPost("/{mentorId:int}/slots", async (
            int mentorId,
            CreateMentorSlotRequest request,
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminUserId = CurrentUserId(http);
                var slot = await mentorService.CreateMentorSlotAsync(mentorId, adminUserId, request, cancellationToken);
                return Results.Created($"/api/admin/mentors/{mentorId}/slots", slot);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPost("/{mentorId:int}/slots/{slotId:int}/close", async (
            int mentorId,
            int slotId,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await mentorService.CloseMentorSlotAsync(mentorId, slotId, cancellationToken);
                return Results.Ok();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        });
    }
}
