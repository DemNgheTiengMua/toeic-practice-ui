using BusinessObjects;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class MentorService
{
    public async Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
    {
        var query = db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .Include(m => m.MentorWeeklySlots)
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
            .Include(m => m.MentorWeeklySlots)
            .FirstOrDefaultAsync(m => m.MentorId == mentorId && m.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Active mentor with id {mentorId} was not found.");

        await EnsureUpcomingWeeklySlotsAsync(mentorId, cancellationToken);

        var rating = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.MentorId == mentorId, cancellationToken);

        var nowUtc = clock.GetUtcNow();
        var closedCode = SlotStatus.Closed.ToCode();
        var slots = await db.MentorSlots
            .AsNoTracking()
            .Where(s => s.MentorId == mentorId && s.Status != closedCode && s.StartAt > nowUtc)
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

        var weeklyDtos = ToWeeklySlotDtos(mentor.MentorWeeklySlots);

        return new MentorDetailDto(ToSummaryDto(mentor, rating), weeklyDtos, slots, reviews);
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

    private static MentorSummaryDto ToSummaryDto(MentorProfile m, vw_MentorAvgRating? r) =>
        new(
            m.MentorId,
            m.DisplayName,
            m.Bio,
            m.CertId,
            m.Cert.Code,
            m.Cert.Name,
            m.SkillId,
            m.Skill?.Code,
            m.Skill?.Name,
            m.Skill?.Modality,
            m.MeetLink,
            m.PriceCredits,
            m.Headline,
            m.Expertise,
            m.SessionMinutes,
            m.IsActive,
            r?.AvgRating,
            r?.ReviewCount ?? 0,
            ToWeeklySlotDtos(m.MentorWeeklySlots));
}
