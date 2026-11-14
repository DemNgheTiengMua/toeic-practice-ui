using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class MentorService
{
    public async Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, int userId, CancellationToken cancellationToken = default)
    {
        var booking = await db.Bookings
            .Include(b => b.Mentor)
            .Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"Booking with id {bookingId} was not found.");

        if (booking.Status == BookingStatus.Cancelled.ToCode())
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRefunded, "This booking has already been cancelled and refunded.");
        }

        var alreadyRefunded = await db.CreditTransactions.AnyAsync(
            t => t.BookingId == bookingId && t.Reason == CreditReason.BookingCancel.ToCode(),
            cancellationToken);
        if (alreadyRefunded)
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRefunded, "This booking has already been cancelled and refunded.");
        }

        if (booking.Status != BookingStatus.Confirmed.ToCode() && booking.Status != BookingStatus.Pending.ToCode())
        {
            throw new MentorBookingException(MentorBookingException.BookingCannotCancel, $"Cannot cancel booking with status '{booking.Status}'.");
        }

        var now = clock.GetUtcNow();
        if (booking.Slot.StartAt <= now)
        {
            throw new MentorBookingException(MentorBookingException.BookingCannotCancel, "Cannot cancel a session that has already started or passed.");
        }

        if (booking.Slot.StartAt - now < TimeSpan.FromHours(24))
        {
            throw new MentorBookingException(MentorBookingException.BookingCannotCancel, "Bookings can only be cancelled at least 24 hours before the session starts.");
        }

        var originalTxn = await db.CreditTransactions
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Reason == CreditReason.MentorBooking.ToCode(), cancellationToken);
        var refundCredits = originalTxn != null ? -originalTxn.Delta : booking.PriceCredits;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.Cancelled.ToCode();
        booking.CancelledAt = now;
        await db.SaveChangesAsync(cancellationToken);
        // trg_Bookings_SyncSlot updates MentorSlots.Status back to 'open' in SQL; keep ChangeTracker in sync.
        await db.Entry(booking.Slot).ReloadAsync(cancellationToken);

        try
        {
            await _creditLedger.RefundBookingAsync(userId, booking.BookingId, refundCredits, cancellationToken);
            // If the freed slot no longer matches an active weekly availability window (e.g. schedule cleared/changed), close it.
            await SyncMentorSlotsWithWeeklyScheduleAsync(booking.MentorId, closeUnmatchedOpenSlots: true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (CreditLedgerException)
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRefunded, "This booking has already been cancelled and refunded.");
        }

        return new CancelBookingResultDto(booking.BookingId, booking.Status, booking.CancelledAt.Value, refundCredits);
    }
}
