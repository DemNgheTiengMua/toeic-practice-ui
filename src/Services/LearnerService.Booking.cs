using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private async Task<UpcomingBookingCard?> BuildUpcomingBookingAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var confirmedCode = BookingStatus.Confirmed.ToCode();
        var humanCode = MentorType.Human.ToCode();
        var now = DateTimeOffset.UtcNow;

        return await db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.UserId == userId &&
                b.Status == confirmedCode &&
                b.MentorType == humanCode &&
                b.Mentor.CertId == certId &&
                b.Slot != null &&
                b.Slot.StartAt > now)
            .OrderBy(b => b.Slot!.StartAt)
            .ThenBy(b => b.BookingId)
            .Select(b => new UpcomingBookingCard(
                b.BookingId,
                b.Mentor.DisplayName,
                b.Mentor.Headline,
                b.FocusArea,
                b.Slot!.StartAt,
                b.Slot.EndAt,
                b.MeetLink))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
