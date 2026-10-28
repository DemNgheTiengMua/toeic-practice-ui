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

        var isHuman = mentor.Type == MentorType.Human.ToCode();
        MentorSlot? slot = null;
        if (isHuman)
        {
            if (!request.SlotId.HasValue)
            {
                throw new ValidationException("A time slot must be selected to book a human mentor.");
            }

            slot = await db.MentorSlots
                .FirstOrDefaultAsync(s => s.SlotId == request.SlotId.Value && s.MentorId == mentorId, cancellationToken);
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
                b => b.SlotId == request.SlotId.Value && b.Status != BookingStatus.Cancelled.ToCode(),
                cancellationToken);
            if (slotAlreadyBooked)
            {
                throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot is already booked.");
            }
        }
        else
        {
            if (request.SlotId.HasValue)
            {
                throw new ValidationException("AI mentors do not use scheduled slots.");
            }
        }

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        if (balance < mentor.PriceCredits)
        {
            throw new MentorBookingException(MentorBookingException.InsufficientCredits, "You do not have enough credits to book this mentor session.");
        }

        SpeakingAttemptInfo? attemptInfo = null;
        if (!isHuman)
        {
            if (speakingPracticeService is null)
            {
                throw new InvalidOperationException("Speaking practice service is required to book an AI mentor.");
            }

            int examId;
            if (request.ExamId.HasValue)
            {
                var exam = await db.Exams.AsNoTracking()
                    .Where(e => e.ExamId == request.ExamId.Value && e.CertId == mentor.CertId && e.Purpose == ExamPurpose.SpeakingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
                    .FirstOrDefaultAsync(cancellationToken);
                if (exam is null)
                {
                    throw new MentorBookingException(MentorBookingException.ExamNotFound, $"Speaking practice exam {request.ExamId.Value} is not valid for certificate {mentor.CertId}.");
                }
                examId = exam.ExamId;
            }
            else
            {
                var exam = await db.Exams.AsNoTracking()
                    .Where(e => e.CertId == mentor.CertId && e.Purpose == ExamPurpose.SpeakingPractice.ToCode() && e.Status == ExamStatus.Published.ToCode() && !e.IsDeleted)
                    .OrderBy(e => e.ExamId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (exam is null)
                {
                    throw new MentorBookingException(MentorBookingException.ExamNotFound, $"No speaking practice exam found for certificate {mentor.CertId}.");
                }
                examId = exam.ExamId;
            }

            if (learners is not null)
            {
                var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
                if (activeCertId != mentor.CertId)
                {
                    await learners.SwitchCertificateAsync(userId, mentor.Cert.Code, cancellationToken);
                }
            }

            try
            {
                attemptInfo = await speakingPracticeService.StartAttemptAsync(userId, examId, cancellationToken);
            }
            catch (SpeakingPracticeException ex)
            {
                throw new MentorBookingException(ex.Code, ex.Message);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var booking = new Booking
        {
            UserId = userId,
            MentorId = mentorId,
            SlotId = isHuman ? request.SlotId : null,
            MentorType = mentor.Type,
            Status = BookingStatus.Confirmed.ToCode(),
            MeetLink = isHuman ? GenerateMeetLink() : null,
            FocusArea = string.IsNullOrWhiteSpace(request.FocusArea) ? null : request.FocusArea.Trim(),
            CancelledAt = null,
            CreatedAt = now
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new MentorBookingException(MentorBookingException.SlotNotAvailable, "The selected slot is no longer available.");
        }

        try
        {
            await _creditLedger.ChargeMentorBookingAsync(userId, booking.BookingId, mentor.PriceCredits, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new MentorBookingException(MentorBookingException.InsufficientCredits, "You do not have enough credits to book this mentor session.");
        }

        int? attemptId = attemptInfo?.AttemptId;
        string? redirectUrl = attemptInfo != null ? $"/speaking/take/{attemptInfo.AttemptId}" : null;

        return ToBookingDto(booking, mentor, slot, attemptId, redirectUrl);
    }

    private static string GenerateMeetLink()
    {
        var random = Random.Shared;
        string Part(int length) => new string(Enumerable.Range(0, length).Select(_ => (char)('a' + random.Next(26))).ToArray());
        return $"https://meet.google.com/{Part(3)}-{Part(4)}-{Part(3)}";
    }
}
