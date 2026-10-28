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
}
