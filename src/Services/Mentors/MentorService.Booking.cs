using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class MentorService
{
    public async Task<MentorBookingDto> BookMentorAsync(int mentorId, int userId, BookMentorRequest request, CancellationToken cancellationToken = default)
    {
        var mentor = await db.MentorProfiles
            .Include(m => m.Cert)
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");

        if (!mentor.IsActive)
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "Cannot book an inactive mentor.");
        }

        if (request.SlotId <= 0)
        {
            throw new ValidationException("A valid time slot must be selected to book a mentor session.");
        }

        await EnsureUpcomingWeeklySlotsAsync(mentorId, cancellationToken);

        var slot = await db.MentorSlots
            .FirstOrDefaultAsync(s => s.SlotId == request.SlotId && s.MentorId == mentorId, cancellationToken);
        if (slot is null || slot.Status != SlotStatus.Open.ToCode())
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot is no longer available.");
        }

        var nowUtc = clock.GetUtcNow();
        if (slot.StartAt <= nowUtc)
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot has already passed.");
        }

        var slotAlreadyBooked = await db.Bookings.AnyAsync(
            b => b.SlotId == request.SlotId && b.Status != BookingStatus.Cancelled.ToCode(),
            cancellationToken);
        if (slotAlreadyBooked)
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot is already booked.");
        }

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        if (balance < mentor.PriceCredits)
        {
            throw new MentorBookingException(MentorBookingException.InsufficientCredits, "You do not have enough credits to book this mentor session.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var booking = new Booking
        {
            UserId = userId,
            MentorId = mentorId,
            SlotId = slot.SlotId,
            PriceCredits = mentor.PriceCredits,
            Status = BookingStatus.Confirmed.ToCode(),
            MeetLink = mentor.MeetLink,
            FocusArea = string.IsNullOrWhiteSpace(request.FocusArea) ? null : request.FocusArea.Trim(),
            CancelledAt = null,
            CreatedAt = nowUtc,
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            // trg_Bookings_SyncSlot updates MentorSlots.Status to 'booked' in SQL; keep ChangeTracker in sync.
            await db.Entry(slot).ReloadAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot is no longer available.");
        }

        try
        {
            await _creditLedger.ChargeMentorBookingAsync(userId, booking.BookingId, booking.PriceCredits, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new MentorBookingException(MentorBookingException.InsufficientCredits, "You do not have enough credits to book this mentor session.");
        }

        var standing = await BuildStandingAsync(userId, mentor.CertId, cancellationToken);

        return ToBookingDto(booking, mentor, slot, standing: standing);
    }
}
