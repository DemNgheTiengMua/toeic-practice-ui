using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    private static void MapBookings(RouteGroupBuilder learnerMentors)
    {

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

        learnerMentors.MapPost("/bookings/{bookingId:int}/rate", async (
            int bookingId,
            RateMentorRequest request,
            HttpContext http,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var userId = CurrentUserId(http);
                var review = await mentorService.RateBookingAsync(bookingId, userId, request, cancellationToken);
                return Results.Ok(review);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (MentorBookingException ex)
            {
                return Problem(ex.Status, "Mentor rating error", ex.Code, ex.Message);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        });
    }
}
