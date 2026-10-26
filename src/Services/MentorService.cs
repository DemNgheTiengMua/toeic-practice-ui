using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public class MentorBookingException(string code, string message, int status = 400) : Exception(message)
{
    public const string InsufficientCredits = ApiError.Codes.InsufficientCredits;
    public const string SlotNotAvailable = ApiError.Codes.SlotNotAvailable;
    public const string BookingNotFound = ApiError.Codes.BookingNotFound;
    public const string BookingCannotCancel = ApiError.Codes.BookingCannotCancel;
    public const string BookingAlreadyRefunded = ApiError.Codes.BookingAlreadyRefunded;
    public const string BookingNotFinished = ApiError.Codes.BookingNotFinished;
    public const string BookingAlreadyRated = ApiError.Codes.BookingAlreadyRated;
    public const string MentorNotFound = ApiError.Codes.MentorNotFound;
    public const string ExamNotFound = ApiError.Codes.ExamNotFound;
    public const string Validation = ApiError.Codes.Validation;

    public string Code { get; } = code;
    public int Status { get; } = status;
}

public interface IMentorService
{
    // Learner browsing
    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default);
    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default);

    // Learner booking & cancellation
    Task<MentorBookingDto> BookMentorAsync(int mentorId, int userId, BookMentorRequest request, CancellationToken cancellationToken = default);
    Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(int userId, CancellationToken cancellationToken = default);
    Task<MentorReviewDto> RateBookingAsync(int bookingId, int userId, RateMentorRequest request, CancellationToken cancellationToken = default);

    // Admin management
    Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default);
    Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, int adminUserId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default);
    Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MentorCertificateOptionDto>> GetCertificateOptionsAsync(CancellationToken cancellationToken = default);
}

public sealed class MentorService(
    CompanionDbContext db,
    TimeProvider clock,
    ILearnerService? learners = null,
    ISpeakingPracticeService? speakingPracticeService = null,
    ICreditLedger? creditLedger = null) : IMentorService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock);
    public async Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
    {
        var query = db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .Where(m => m.IsActive);

        if (!string.IsNullOrWhiteSpace(certCode))
        {
            var certNormalized = certCode.Trim();
            query = query.Where(m => m.Cert.Code == certNormalized);
        }

        if (!string.IsNullOrWhiteSpace(skill) && !skill.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var skillNormalized = skill.Trim().ToLowerInvariant();
            query = query.Where(m => m.Skill != null && (m.Skill.Modality.ToLower() == skillNormalized || m.Skill.Code.ToLower() == skillNormalized));
        }

        var mentors = await query
            .OrderBy(m => m.MentorId)
            .ToListAsync(cancellationToken);

        var mentorIds = mentors.Select(m => m.MentorId).ToList();
        var ratings = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .Where(r => mentorIds.Contains(r.MentorId))
            .ToDictionaryAsync(r => r.MentorId, cancellationToken);

        return mentors.Select(m =>
        {
            ratings.TryGetValue(m.MentorId, out var rating);
            return ToSummaryDto(m, rating);
        }).ToList();
    }

    public async Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        var mentor = await db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .FirstOrDefaultAsync(m => m.MentorId == mentorId && m.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Active mentor with id {mentorId} was not found.");

        var rating = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.MentorId == mentorId, cancellationToken);

        var nowUtc = clock.GetUtcNow();
        var slots = await db.MentorSlots
            .AsNoTracking()
            .Where(s => s.MentorId == mentorId && s.Status == SlotStatus.Open.ToCode() && s.StartAt > nowUtc)
            .OrderBy(s => s.StartAt)
            .Select(s => new MentorSlotDto(s.SlotId, s.MentorId, s.StartAt, s.EndAt, s.Status))
            .ToListAsync(cancellationToken);

        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.Booking.MentorId == mentorId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .Select(r => new MentorReviewDto(
                r.ReviewId,
                r.Rating,
                r.Text,
                r.User.FullName ?? "Anonymous",
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return new MentorDetailDto(ToSummaryDto(mentor, rating), slots, reviews);
    }

    public async Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default)
    {
        var mentors = await db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .Include(m => m.MentorSlots)
            .OrderBy(m => m.MentorId)
            .ToListAsync(cancellationToken);

        var mentorIds = mentors.Select(m => m.MentorId).ToList();
        var ratings = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .Where(r => mentorIds.Contains(r.MentorId))
            .ToDictionaryAsync(r => r.MentorId, cancellationToken);

        var nowUtc = clock.GetUtcNow();

        return mentors.Select(m =>
        {
            ratings.TryGetValue(m.MentorId, out var rating);
            var totalSlots = m.MentorSlots.Count;
            var openSlots = m.MentorSlots.Count(s => s.Status == SlotStatus.Open.ToCode() && s.StartAt > nowUtc);

            return new AdminMentorListItemDto(
                m.MentorId,
                m.UserId,
                m.DisplayName,
                m.Type,
                m.CertId,
                m.Cert.Code,
                m.SkillId,
                m.Skill?.Name,
                m.PricePerSlot,
                m.PriceCredits,
                m.SessionMinutes,
                rating?.AvgRating,
                rating?.ReviewCount ?? 0,
                totalSlots,
                openSlots,
                m.IsActive);
        }).ToList();
    }

    public async Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        var mentor = await db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");

        var rating = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.MentorId == mentorId, cancellationToken);

        return ToSummaryDto(mentor, rating);
    }

    public async Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateMentorInputAsync(
            request.DisplayName,
            request.Type,
            request.UserId,
            request.CertId,
            request.SkillId,
            request.PricePerSlot,
            request.PriceCredits,
            request.SessionMinutes,
            cancellationToken);

        var profile = new MentorProfile
        {
            DisplayName = request.DisplayName.Trim(),
            Type = request.Type.Trim().ToLowerInvariant(),
            UserId = request.Type.Equals(MentorType.Human.ToCode(), StringComparison.OrdinalIgnoreCase) ? request.UserId : null,
            Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim(),
            CertId = request.CertId,
            SkillId = request.SkillId,
            PricePerSlot = request.PricePerSlot,
            PriceCredits = request.PriceCredits,
            Headline = string.IsNullOrWhiteSpace(request.Headline) ? null : request.Headline.Trim(),
            Expertise = string.IsNullOrWhiteSpace(request.Expertise) ? null : request.Expertise.Trim(),
            SessionMinutes = request.SessionMinutes > 0 ? request.SessionMinutes : 60,
            IsActive = request.IsActive,
        };

        db.MentorProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAdminMentorByIdAsync(profile.MentorId, cancellationToken);
    }

    public async Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await db.MentorProfiles
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");

        await ValidateMentorInputAsync(
            request.DisplayName,
            profile.Type,
            request.UserId,
            request.CertId,
            request.SkillId,
            request.PricePerSlot,
            request.PriceCredits,
            request.SessionMinutes,
            cancellationToken);

        profile.DisplayName = request.DisplayName.Trim();
        profile.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        profile.CertId = request.CertId;
        profile.SkillId = request.SkillId;
        if (profile.Type == MentorType.Human.ToCode())
        {
            profile.UserId = request.UserId;
        }
        else
        {
            profile.UserId = null;
        }
        profile.PricePerSlot = request.PricePerSlot;
        profile.PriceCredits = request.PriceCredits;
        profile.Headline = string.IsNullOrWhiteSpace(request.Headline) ? null : request.Headline.Trim();
        profile.Expertise = string.IsNullOrWhiteSpace(request.Expertise) ? null : request.Expertise.Trim();
        profile.SessionMinutes = request.SessionMinutes > 0 ? request.SessionMinutes : 60;
        profile.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return await GetAdminMentorByIdAsync(profile.MentorId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        var mentorExists = await db.MentorProfiles.AnyAsync(m => m.MentorId == mentorId, cancellationToken);
        if (!mentorExists)
        {
            throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");
        }

        return await db.MentorSlots
            .AsNoTracking()
            .Where(s => s.MentorId == mentorId)
            .OrderBy(s => s.StartAt)
            .Select(s => new AdminMentorSlotDto(s.SlotId, s.MentorId, s.StartAt, s.EndAt, s.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, int adminUserId, CreateMentorSlotRequest request, CancellationToken cancellationToken = default)
    {
        var mentor = await db.MentorProfiles
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");

        if (!mentor.IsActive)
        {
            throw new ValidationException("Cannot schedule slots for an inactive mentor.");
        }

        if (mentor.Type == MentorType.Ai.ToCode())
        {
            throw new ValidationException("AI mentors do not use scheduled slots.");
        }

        var adminUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == adminUserId, cancellationToken)
            ?? throw new KeyNotFoundException($"Admin user with id {adminUserId} was not found.");

        var tz = ResolveTimeZone(request.TimeZoneId, adminUser.TimeZoneId);
        var unspecifiedStart = DateTime.SpecifyKind(request.LocalStartAt, DateTimeKind.Unspecified);
        var startOffset = tz.GetUtcOffset(unspecifiedStart);
        var startUtc = new DateTimeOffset(unspecifiedStart, startOffset).ToUniversalTime();

        DateTimeOffset endUtc;
        if (request.LocalEndAt.HasValue)
        {
            var unspecifiedEnd = DateTime.SpecifyKind(request.LocalEndAt.Value, DateTimeKind.Unspecified);
            var endOffset = tz.GetUtcOffset(unspecifiedEnd);
            endUtc = new DateTimeOffset(unspecifiedEnd, endOffset).ToUniversalTime();
        }
        else
        {
            endUtc = startUtc.AddMinutes(mentor.SessionMinutes > 0 ? mentor.SessionMinutes : 60);
        }

        if (endUtc <= startUtc)
        {
            throw new ValidationException("End time must be after start time.");
        }

        var hasOverlap = await db.MentorSlots.AnyAsync(
            s => s.MentorId == mentorId &&
                 s.Status != SlotStatus.Closed.ToCode() &&
                 s.StartAt < endUtc &&
                 s.EndAt > startUtc,
            cancellationToken);

        if (hasOverlap)
        {
            throw new ValidationException("The specified time slot overlaps with an existing slot for this mentor.");
        }

        var slot = new MentorSlot
        {
            MentorId = mentorId,
            StartAt = startUtc,
            EndAt = endUtc,
            Status = SlotStatus.Open.ToCode(),
        };

        db.MentorSlots.Add(slot);
        await db.SaveChangesAsync(cancellationToken);

        return new AdminMentorSlotDto(slot.SlotId, slot.MentorId, slot.StartAt, slot.EndAt, slot.Status);
    }

    public async Task CloseMentorSlotAsync(int mentorId, int slotId, CancellationToken cancellationToken = default)
    {
        var slot = await db.MentorSlots
            .FirstOrDefaultAsync(s => s.SlotId == slotId && s.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Slot with id {slotId} for mentor {mentorId} was not found.");

        if (slot.Status == SlotStatus.Booked.ToCode())
        {
            throw new ValidationException("Cannot close a slot that has already been booked.");
        }

        slot.Status = SlotStatus.Closed.ToCode();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MentorCertificateOptionDto>> GetCertificateOptionsAsync(CancellationToken cancellationToken = default)
    {
        var certificates = await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CertId)
            .ToListAsync(cancellationToken);

        var certIds = certificates.Select(c => c.CertId).ToList();

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => certIds.Contains(s.CertId))
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        return certificates.Select(c => new MentorCertificateOptionDto(
            c.CertId,
            c.Code,
            c.Name,
            skills.Where(s => s.CertId == c.CertId)
                  .Select(s => new MentorSkillOptionDto(s.SkillId, s.CertId, s.Code, s.Name, s.Modality))
                  .ToList())).ToList();
    }

    private static TimeZoneInfo ResolveTimeZone(string? preferredZoneId, string? fallbackZoneId)
    {
        var candidates = new[] { preferredZoneId, fallbackZoneId, TimeZones.DefaultId, "UTC" };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var tz)) return tz;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(candidate, out var winId) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(winId, out tz)) return tz;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out var ianaId) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out tz)) return tz;
        }
        return TimeZoneInfo.Utc;
    }

    private async Task ValidateMentorInputAsync(
        string displayName,
        string type,
        int? userId,
        int certId,
        int? skillId,
        decimal pricePerSlot,
        int priceCredits,
        int sessionMinutes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ValidationException("Display name is required.");
        }

        if (!type.Equals(MentorType.Human.ToCode(), StringComparison.OrdinalIgnoreCase) &&
            !type.Equals(MentorType.Ai.ToCode(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Invalid mentor type '{type}'. Allowed values: human, ai.");
        }

        var isHuman = type.Equals(MentorType.Human.ToCode(), StringComparison.OrdinalIgnoreCase);
        if (isHuman)
        {
            if (!userId.HasValue || userId.Value <= 0)
            {
                throw new ValidationException("Human mentors require an associated user account.");
            }

            var userExists = await db.Users.AnyAsync(u => u.UserId == userId.Value, cancellationToken);
            if (!userExists)
            {
                throw new ValidationException($"User with id {userId.Value} does not exist.");
            }
        }
        else
        {
            if (userId.HasValue)
            {
                throw new ValidationException("AI mentors cannot have an associated user account.");
            }
        }

        var certExists = await db.Certificates.AnyAsync(c => c.CertId == certId, cancellationToken);
        if (!certExists)
        {
            throw new ValidationException($"Certificate with id {certId} does not exist.");
        }

        if (skillId.HasValue)
        {
            var skill = await db.Skills.AsNoTracking().FirstOrDefaultAsync(s => s.SkillId == skillId.Value, cancellationToken);
            if (skill is null)
            {
                throw new ValidationException($"Skill with id {skillId.Value} does not exist.");
            }

            if (skill.CertId != certId)
            {
                throw new ValidationException($"Skill '{skill.Name}' belongs to certificate {skill.CertId}, not {certId}.");
            }
        }

        if (pricePerSlot < 0)
        {
            throw new ValidationException("Price per slot cannot be negative.");
        }

        if (priceCredits <= 0)
        {
            throw new ValidationException("Price in credits must be at least 1.");
        }

        if (sessionMinutes <= 0)
        {
            throw new ValidationException("Session minutes must be greater than zero.");
        }
    }

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
        if (booking.Slot != null && booking.Slot.StartAt <= now)
        {
            throw new MentorBookingException(MentorBookingException.BookingCannotCancel, "Cannot cancel a session that has already started or passed.");
        }

        var originalTxn = await db.CreditTransactions
            .FirstOrDefaultAsync(t => t.BookingId == bookingId && t.Reason == CreditReason.MentorBooking.ToCode(), cancellationToken);
        var refundCredits = originalTxn != null ? -originalTxn.Delta : booking.Mentor.PriceCredits;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.Cancelled.ToCode();
        booking.CancelledAt = now;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await _creditLedger.RefundBookingAsync(userId, booking.BookingId, refundCredits, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (CreditLedgerException)
        {
            throw new MentorBookingException(MentorBookingException.BookingAlreadyRefunded, "This booking has already been cancelled and refunded.");
        }

        return new CancelBookingResultDto(booking.BookingId, booking.Status, booking.CancelledAt.Value, refundCredits);
    }

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

    private static string GenerateMeetLink()
    {
        var random = Random.Shared;
        string Part(int length) => new string(Enumerable.Range(0, length).Select(_ => (char)('a' + random.Next(26))).ToArray());
        return $"https://meet.google.com/{Part(3)}-{Part(4)}-{Part(3)}";
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

    private static MentorSummaryDto ToSummaryDto(MentorProfile m, vw_MentorAvgRating? r) =>
        new(
            m.MentorId,
            m.UserId,
            m.DisplayName,
            m.Bio,
            m.CertId,
            m.Cert.Code,
            m.Cert.Name,
            m.SkillId,
            m.Skill?.Code,
            m.Skill?.Name,
            m.Skill?.Modality,
            m.Type,
            m.PricePerSlot,
            m.PriceCredits,
            m.Headline,
            m.Expertise,
            m.SessionMinutes,
            m.IsActive,
            r?.AvgRating,
            r?.ReviewCount ?? 0);
}
