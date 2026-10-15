using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class MentorEndpoints
{
    public static void MapMentorEndpoints(this IEndpointRouteBuilder app)
    {
        // Public / learner endpoints
        app.MapGet("/api/mentors", async (
            [FromQuery] string? certCode,
            [FromQuery] string? skill,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            var mentors = await mentorService.GetMentorsAsync(certCode, skill, cancellationToken);
            return Results.Ok(mentors);
        });

        app.MapGet("/api/mentors/{mentorId:int}", async (
            int mentorId,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await mentorService.GetMentorDetailAsync(mentorId, cancellationToken);
                return Results.Ok(detail);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Learner booking endpoints
        var learnerMentors = app.MapGroup("/api/mentors").RequireAuthorization(Roles.Learner);

        learnerMentors.MapPost("/{mentorId:int}/book", async (
            int mentorId,
            BookMentorRequest request,
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var userId = CurrentUserId(http);
                var booking = await mentorService.BookMentorAsync(mentorId, userId, request, cancellationToken);
                return Results.Created($"/api/mentors/bookings/{booking.BookingId}", booking);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (MentorBookingException ex)
            {
                return Problem(ex.Status, "Mentor booking error", ex.Code, ex.Message);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        });

        learnerMentors.MapPost("/bookings/{bookingId:int}/cancel", async (
            int bookingId,
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var userId = CurrentUserId(http);
                var result = await mentorService.CancelBookingAsync(bookingId, userId, cancellationToken);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (MentorBookingException ex)
            {
                return Problem(ex.Status, "Mentor booking cancellation error", ex.Code, ex.Message);
            }
        });

        learnerMentors.MapGet("/bookings/{bookingId:int}", async (
            int bookingId,
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var userId = CurrentUserId(http);
                var booking = await mentorService.GetBookingDetailAsync(bookingId, userId, cancellationToken);
                return Results.Ok(booking);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        learnerMentors.MapGet("/bookings", async (
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            var userId = CurrentUserId(http);
            var bookings = await mentorService.GetLearnerBookingsAsync(userId, cancellationToken);
            return Results.Ok(bookings);
        });

        // Admin endpoints
        var admin = app.MapGroup("/api/admin/mentors").RequireAuthorization(Roles.Admin);

        admin.MapGet("", async (IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            var list = await mentorService.GetAdminMentorsAsync(cancellationToken);
            return Results.Ok(list);
        });

        admin.MapGet("/options", async (IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            var options = await mentorService.GetCertificateOptionsAsync(cancellationToken);
            return Results.Ok(options);
        });

        admin.MapGet("/{mentorId:int}", async (int mentorId, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var mentor = await mentorService.GetAdminMentorByIdAsync(mentorId, cancellationToken);
                return Results.Ok(mentor);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapPost("", async (CreateMentorProfileRequest request, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await mentorService.CreateMentorAsync(request, cancellationToken);
                return Results.Created($"/api/admin/mentors/{created.MentorId}", created);
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

        admin.MapPut("/{mentorId:int}", async (int mentorId, UpdateMentorProfileRequest request, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await mentorService.UpdateMentorAsync(mentorId, request, cancellationToken);
                return Results.Ok(updated);
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
