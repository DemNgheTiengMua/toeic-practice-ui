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

    public async Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        var booking = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Mentor)
            .Include(b => b.Slot)
            .Include(b => b.User)
            .Include(b => b.Reviews)
                .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"Booking with id {bookingId} was not found.");

        var review = booking.Reviews.FirstOrDefault();
        var reviewDto = review != null
            ? new MentorReviewDto(
                review.ReviewId,
                review.Rating,
                review.Text,
                review.User?.FullName ?? booking.User?.FullName ?? "Anonymous",
                review.CreatedAt)
            : null;

        return ToBookingDto(booking, booking.Mentor, booking.Slot, review: reviewDto);
    }

    public async Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var bookings = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Mentor)
            .Include(b => b.Slot)
            .Include(b => b.User)
            .Include(b => b.Reviews)
                .ThenInclude(r => r.User)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        return bookings.Select(b =>
        {
            var review = b.Reviews.FirstOrDefault();
            var reviewDto = review != null
                ? new MentorReviewDto(
                    review.ReviewId,
                    review.Rating,
                    review.Text,
                    review.User?.FullName ?? b.User?.FullName ?? "Anonymous",
                    review.CreatedAt)
                : null;
            return ToBookingDto(b, b.Mentor, b.Slot, review: reviewDto);
        }).ToList();
    }

    private static MentorBookingDto ToBookingDto(Booking b, MentorProfile mentor, MentorSlot? slot, int? attemptId = null, string? redirectUrl = null, MentorReviewDto? review = null) =>
        new(
            b.BookingId,
            b.UserId,
            b.MentorId,
            mentor.DisplayName,
            mentor.Headline,
            b.MentorType,
            b.SlotId,
            slot?.StartAt,
            slot?.EndAt,
            b.Status,
            b.MeetLink,
            b.FocusArea,
            mentor.PriceCredits,
            b.CancelledAt,
            b.CreatedAt,
            attemptId,
            redirectUrl,
            review);
}
