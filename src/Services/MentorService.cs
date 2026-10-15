using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IMentorService
{
    // Learner browsing
    Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default);
    Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default);

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

public sealed class MentorService(CompanionDbContext db, TimeProvider clock) : IMentorService
{
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
