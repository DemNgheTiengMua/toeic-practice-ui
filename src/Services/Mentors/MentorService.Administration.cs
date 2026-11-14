using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class MentorService
{
    public async Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(CancellationToken cancellationToken = default)
    {
        var activeMentorIds = await db.MentorProfiles
            .AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => m.MentorId)
            .ToListAsync(cancellationToken);

        foreach (var activeId in activeMentorIds)
        {
            await EnsureUpcomingWeeklySlotsAsync(activeId, cancellationToken);
        }

        var mentors = await db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .Include(m => m.MentorSlots)
            .Include(m => m.MentorWeeklySlots)
            .OrderBy(m => m.MentorId)
            .ToListAsync(cancellationToken);

        var mentorIds = mentors.Select(m => m.MentorId).ToList();
        var ratings = await db.vw_MentorAvgRatings
            .AsNoTracking()
            .Where(r => mentorIds.Contains(r.MentorId))
            .ToDictionaryAsync(r => r.MentorId, cancellationToken);

        var nowUtc = clock.GetUtcNow();
        var cancelledCode = BookingStatus.Cancelled.ToCode();
        var noShowType = ComplaintType.MentorNoShow.ToCode();
        var resolvedStatus = ComplaintStatus.Resolved.ToCode();

        var completedBookings = await db.Bookings
            .AsNoTracking()
            .Where(b =>
                mentorIds.Contains(b.MentorId) &&
                b.Status != cancelledCode &&
                b.Slot.EndAt <= nowUtc &&
                !b.Complaints.Any(c =>
                    c.Type == noShowType &&
                    c.Status == resolvedStatus &&
                    c.ResolvedByGrantTxnId != null))
            .Select(b => new { b.MentorId, b.PriceCredits })
            .ToListAsync(cancellationToken);

        var earningsByMentor = completedBookings
            .GroupBy(b => b.MentorId)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), Credits: g.Sum(x => x.PriceCredits)));

        var openCode = SlotStatus.Open.ToCode();

        return mentors.Select(m =>
        {
            ratings.TryGetValue(m.MentorId, out var rating);
            earningsByMentor.TryGetValue(m.MentorId, out var earned);
            var totalSlots = m.MentorSlots.Count;
            var openSlots = m.MentorSlots.Count(s => s.Status == openCode && s.StartAt > nowUtc);
            var weeklyDtos = ToWeeklySlotDtos(m.MentorWeeklySlots);

            return new AdminMentorListItemDto(
                m.MentorId,
                m.DisplayName,
                m.CertId,
                m.Cert.Code,
                m.SkillId,
                m.Skill?.Name,
                m.MeetLink,
                m.PriceCredits,
                CreditExchangeRate.ToVnd(m.PriceCredits),
                m.SessionMinutes,
                rating?.AvgRating,
                rating?.ReviewCount ?? 0,
                totalSlots,
                openSlots,
                earned.Count,
                earned.Credits,
                CreditExchangeRate.ToVnd(earned.Credits),
                weeklyDtos,
                m.IsActive);
        }).ToList();
    }

    public async Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, CancellationToken cancellationToken = default)
    {
        var mentor = await db.MentorProfiles
            .AsNoTracking()
            .Include(m => m.Cert)
            .Include(m => m.Skill)
            .Include(m => m.MentorWeeklySlots)
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
            request.CertId,
            request.SkillId,
            request.MeetLink,
            request.PriceCredits,
            request.SessionMinutes,
            cancellationToken);

        var parsedWeekly = request.WeeklySlots is not null
            ? ParseAndValidateWeeklySlots(request.WeeklySlots)
            : [];

        var profile = new MentorProfile
        {
            DisplayName = request.DisplayName.Trim(),
            Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim(),
            CertId = request.CertId,
            SkillId = request.SkillId,
            MeetLink = request.MeetLink.Trim(),
            PriceCredits = request.PriceCredits,
            Headline = string.IsNullOrWhiteSpace(request.Headline) ? null : request.Headline.Trim(),
            Expertise = string.IsNullOrWhiteSpace(request.Expertise) ? null : request.Expertise.Trim(),
            SessionMinutes = request.SessionMinutes > 0 ? request.SessionMinutes : 60,
            IsActive = request.IsActive,
        };

        db.MentorProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        if (parsedWeekly.Count > 0)
        {
            foreach (var (dayOfWeek, startTime, endTime) in parsedWeekly)
            {
                db.MentorWeeklySlots.Add(new MentorWeeklySlot
                {
                    MentorId = profile.MentorId,
                    DayOfWeek = dayOfWeek,
                    StartTime = startTime,
                    EndTime = endTime,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await SyncMentorSlotsWithWeeklyScheduleAsync(profile.MentorId, closeUnmatchedOpenSlots: true, cancellationToken);
        }

        return await GetAdminMentorByIdAsync(profile.MentorId, cancellationToken);
    }

    public async Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await db.MentorProfiles
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");

        await ValidateMentorInputAsync(
            request.DisplayName,
            request.CertId,
            request.SkillId,
            request.MeetLink,
            request.PriceCredits,
            request.SessionMinutes,
            cancellationToken);

        var parsedWeekly = request.WeeklySlots is not null
            ? ParseAndValidateWeeklySlots(request.WeeklySlots)
            : null;

        profile.DisplayName = request.DisplayName.Trim();
        profile.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        profile.CertId = request.CertId;
        profile.SkillId = request.SkillId;
        profile.MeetLink = request.MeetLink.Trim();
        profile.PriceCredits = request.PriceCredits;
        profile.Headline = string.IsNullOrWhiteSpace(request.Headline) ? null : request.Headline.Trim();
        profile.Expertise = string.IsNullOrWhiteSpace(request.Expertise) ? null : request.Expertise.Trim();
        profile.SessionMinutes = request.SessionMinutes > 0 ? request.SessionMinutes : 60;
        profile.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        if (parsedWeekly is not null)
        {
            await ApplyWeeklySlotsAsync(mentorId, parsedWeekly, cancellationToken);
        }
        else if (profile.IsActive)
        {
            await EnsureUpcomingWeeklySlotsAsync(mentorId, cancellationToken);
        }

        return await GetAdminMentorByIdAsync(profile.MentorId, cancellationToken);
    }

    public async Task<AdminMentorPayoutsReportDto> GetAdminMentorPayoutsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ValidationException("End date must be on or after start date.");
        }

        var rows = await QueryCompletedPayoutBookingsAsync(from, to, cancellationToken);

        var sessions = rows.Select(r => new AdminMentorPayoutItemDto(
            r.BookingId,
            r.SessionDate,
            r.StartAt,
            r.EndAt,
            r.MentorId,
            r.MentorName,
            r.LearnerName,
            r.LearnerEmail,
            r.CertCode,
            r.SkillName,
            r.PriceCredits,
            r.AmountVnd)).ToList();

        var mentorSummaries = sessions
            .GroupBy(s => new { s.MentorId, s.MentorName, s.CertCode })
            .Select(g => new AdminMentorPayoutMentorSummaryDto(
                g.Key.MentorId,
                g.Key.MentorName,
                g.Key.CertCode,
                g.Count(),
                g.Sum(x => x.PriceCredits),
                g.Sum(x => x.AmountVnd)))
            .OrderByDescending(m => m.TotalAmountVnd)
            .ThenBy(m => m.MentorId)
            .ToList();

        var totalSessions = sessions.Count;
        var totalCredits = sessions.Sum(s => s.PriceCredits);
        var totalAmountVnd = sessions.Sum(s => s.AmountVnd);

        return new AdminMentorPayoutsReportDto(
            from,
            to,
            CreditExchangeRate.VndPerCredit,
            totalSessions,
            totalCredits,
            totalAmountVnd,
            mentorSummaries,
            sessions);
    }

    public async Task<IReadOnlyList<MentorPayoutExportRow>> GetMentorPayoutExportRowsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ValidationException("End date must be on or after start date.");
        }

        var rows = await QueryCompletedPayoutBookingsAsync(from, to, cancellationToken);

        return rows.Select(r => new MentorPayoutExportRow(
            r.BookingId,
            r.SessionDate,
            r.StartTime,
            r.EndTime,
            r.MentorId,
            r.MentorName,
            r.CertCode,
            r.SkillName,
            r.LearnerEmail,
            r.LearnerName,
            r.PriceCredits,
            r.AmountVnd)).ToList();
    }

    private sealed record PayoutBookingProjection(
        int BookingId,
        DateOnly SessionDate,
        TimeOnly StartTime,
        TimeOnly EndTime,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        int MentorId,
        string MentorName,
        string CertCode,
        string? SkillName,
        string LearnerEmail,
        string LearnerName,
        int PriceCredits,
        decimal AmountVnd);

    private async Task<List<PayoutBookingProjection>> QueryCompletedPayoutBookingsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var windowStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), ReportingPeriod.UtcOffset);
        var windowEnd = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), ReportingPeriod.UtcOffset);
        var nowUtc = clock.GetUtcNow();

        var cancelledCode = BookingStatus.Cancelled.ToCode();
        var noShowType = ComplaintType.MentorNoShow.ToCode();
        var resolvedStatus = ComplaintStatus.Resolved.ToCode();

        var raw = await db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.Status != cancelledCode &&
                b.Slot.EndAt <= nowUtc &&
                b.Slot.EndAt >= windowStart &&
                b.Slot.EndAt < windowEnd &&
                !b.Complaints.Any(c =>
                    c.Type == noShowType &&
                    c.Status == resolvedStatus &&
                    c.ResolvedByGrantTxnId != null))
            .OrderByDescending(b => b.Slot.EndAt)
            .ThenByDescending(b => b.BookingId)
            .Select(b => new
            {
                b.BookingId,
                b.Slot.StartAt,
                b.Slot.EndAt,
                b.MentorId,
                MentorName = b.Mentor.DisplayName,
                CertCode = b.Mentor.Cert.Code,
                SkillName = b.Mentor.Skill != null ? b.Mentor.Skill.Name : null,
                LearnerEmail = b.User.Email,
                LearnerName = b.User.FullName ?? b.User.Email,
                b.PriceCredits,
            })
            .ToListAsync(cancellationToken);

        return raw.Select(b =>
        {
            var localStart = ReportingPeriod.LocalInstant(b.StartAt);
            var localEnd = ReportingPeriod.LocalInstant(b.EndAt);
            return new PayoutBookingProjection(
                b.BookingId,
                DateOnly.FromDateTime(localStart.DateTime),
                TimeOnly.FromDateTime(localStart.DateTime),
                TimeOnly.FromDateTime(localEnd.DateTime),
                b.StartAt,
                b.EndAt,
                b.MentorId,
                b.MentorName,
                b.CertCode,
                b.SkillName,
                b.LearnerEmail,
                b.LearnerName,
                b.PriceCredits,
                CreditExchangeRate.ToVnd(b.PriceCredits));
        }).ToList();
    }

    private async Task ValidateMentorInputAsync(
        string displayName,
        int certId,
        int? skillId,
        string meetLink,
        int priceCredits,
        int sessionMinutes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ValidationException("Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(meetLink) ||
            !Uri.TryCreate(meetLink.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationException("A valid HTTP/HTTPS Meet link is required.");
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
