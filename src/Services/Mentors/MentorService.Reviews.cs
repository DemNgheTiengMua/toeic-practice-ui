using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class MentorService
{

    public async Task<MentorReviewDto> RateBookingAsync(int bookingId, int userId, RateMentorRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new ValidationException("Rating must be between 1 and 5.");
        }

        if (request.Text is not null && request.Text.Length > 2000)
        {
            throw new ValidationException("Review text cannot exceed 2000 characters.");
        }

        var booking = await db.Bookings
            .AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.Reviews)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"Booking with id {bookingId} was not found.");

        if (booking.Status != BookingStatus.Done.ToCode())
        {
            throw new MentorBookingException(MentorBookingException.BookingNotFinished, "Only completed bookings can be rated.");
        }

        var alreadyRated = booking.Reviews.Any() || await db.Reviews.AnyAsync(r => r.BookingId == bookingId, cancellationToken);
        if (alreadyRated)
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRated, "This booking has already been rated.");
        }

        var now = clock.GetUtcNow();
        var review = new Review
        {
            BookingId = bookingId,
            UserId = userId,
            Rating = request.Rating,
            Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(),
            CreatedAt = now,
        };

        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRated, "This booking has already been rated.");
        }

        return new MentorReviewDto(
            review.ReviewId,
            review.Rating,
            review.Text,
            booking.User?.FullName ?? "Anonymous",
            review.CreatedAt);
    }
}
