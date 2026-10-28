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
}
