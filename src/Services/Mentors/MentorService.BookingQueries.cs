using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

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

        var standing = await BuildStandingAsync(booking.UserId, booking.Mentor.CertId, cancellationToken);

        return ToBookingDto(booking, booking.Mentor, booking.Slot, review: reviewDto, standing: standing);
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

        var standings = await BuildStandingsAsync(bookings, cancellationToken);

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
            standings.TryGetValue((b.UserId, b.Mentor.CertId), out var standing);
            return ToBookingDto(b, b.Mentor, b.Slot, review: reviewDto, standing: standing);
        }).ToList();
    }

    /// <summary>
    /// #381: staff see every booking in any state, unlike the payout report which only ever sees
    /// delivered sessions. Status is a nullable filter rather than an enum so an unknown code from
    /// a query string returns an empty list instead of throwing.
    /// </summary>
    public async Task<IReadOnlyList<AdminMentorBookingDto>> GetAdminBookingsAsync(
        int? mentorId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = db.Bookings
            .AsNoTracking()
            .AsQueryable();

        if (mentorId is { } id)
        {
            query = query.Where(b => b.MentorId == id);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var code = status.Trim().ToLowerInvariant();
            query = query.Where(b => b.Status == code);
        }

        return await query
            .OrderByDescending(b => b.Slot.StartAt)
            .ThenByDescending(b => b.BookingId)
            .Select(b => new AdminMentorBookingDto(
                b.BookingId,
                b.MentorId,
                b.Mentor.DisplayName,
                b.UserId,
                b.User.FullName ?? b.User.Email,
                b.User.Email,
                b.Slot.StartAt,
                b.Slot.EndAt,
                b.Status,
                b.FocusArea,
                b.PriceCredits,
                b.CancelledAt,
                b.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static MentorBookingDto ToBookingDto(
        Booking b,
        MentorProfile mentor,
        MentorSlot slot,
        MentorReviewDto? review = null,
        MentorLearnerStandingDto? standing = null) =>
        new(
            b.BookingId,
            b.UserId,
            b.MentorId,
            mentor.DisplayName,
            mentor.Headline,
            b.SlotId,
            slot.StartAt,
            slot.EndAt,
            b.Status,
            b.MeetLink ?? mentor.MeetLink,
            b.FocusArea,
            b.PriceCredits,
            b.CancelledAt,
            b.CreatedAt,
            review,
            standing);
}
