using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class MentorService
{
    private static readonly string[] AllowedTimeFormats = ["HH:mm", "H:mm", "HH:mm:ss"];

    public async Task<IReadOnlyList<MentorWeeklySlotDto>> GetAdminMentorWeeklySlotsAsync(
        int mentorId,
        CancellationToken cancellationToken = default)
    {
        var mentorExists = await db.MentorProfiles.AnyAsync(m => m.MentorId == mentorId, cancellationToken);
        if (!mentorExists)
        {
            throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");
        }

        var slots = await db.MentorWeeklySlots
            .AsNoTracking()
            .Where(w => w.MentorId == mentorId)
            .OrderBy(w => w.DayOfWeek)
            .ThenBy(w => w.StartTime)
            .ToListAsync(cancellationToken);

        return ToWeeklySlotDtos(slots);
    }

    public async Task<IReadOnlyList<MentorWeeklySlotDto>> ReplaceMentorWeeklySlotsAsync(
        int mentorId,
        IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots,
        CancellationToken cancellationToken = default)
    {
        var mentorExists = await db.MentorProfiles.AnyAsync(m => m.MentorId == mentorId, cancellationToken);
        if (!mentorExists)
        {
            throw new KeyNotFoundException($"Mentor with id {mentorId} was not found.");
        }

        var parsed = ParseAndValidateWeeklySlots(weeklySlots ?? []);
        await ApplyWeeklySlotsAsync(mentorId, parsed, cancellationToken);

        return await GetAdminMentorWeeklySlotsAsync(mentorId, cancellationToken);
    }

    private static List<(byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime)> ParseAndValidateWeeklySlots(
        IReadOnlyList<MentorWeeklySlotInputDto> inputs)
    {
        var parsed = new List<(byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime)>(inputs.Count);

        foreach (var item in inputs)
        {
            if (item.DayOfWeek < 1 || item.DayOfWeek > 7)
            {
                throw new ValidationException($"DayOfWeek must be between 1 (Monday) and 7 (Sunday), got {item.DayOfWeek}.");
            }

            if (string.IsNullOrWhiteSpace(item.StartTime) ||
                !TimeOnly.TryParseExact(item.StartTime.Trim(), AllowedTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            {
                throw new ValidationException($"Invalid start time '{item.StartTime}'. Expected HH:mm.");
            }

            if (string.IsNullOrWhiteSpace(item.EndTime) ||
                !TimeOnly.TryParseExact(item.EndTime.Trim(), AllowedTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                throw new ValidationException($"Invalid end time '{item.EndTime}'. Expected HH:mm.");
            }

            if (end <= start)
            {
                throw new ValidationException($"End time ({item.EndTime}) must be after start time ({item.StartTime}).");
            }

            parsed.Add(((byte)item.DayOfWeek, start, end));
        }

        foreach (var dayGroup in parsed.GroupBy(x => x.DayOfWeek))
        {
            var ordered = dayGroup.OrderBy(x => x.StartTime).ThenBy(x => x.EndTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];
                if (curr.StartTime < prev.EndTime)
                {
                    throw new ValidationException(
                        $"Weekly availability windows on day {dayGroup.Key} overlap ({prev.StartTime:HH\\:mm}-{prev.EndTime:HH\\:mm} and {curr.StartTime:HH\\:mm}-{curr.EndTime:HH\\:mm}).");
                }
            }
        }

        return parsed
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .ToList();
    }

    private async Task ApplyWeeklySlotsAsync(
        int mentorId,
        List<(byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime)> parsed,
        CancellationToken cancellationToken)
    {
        var existing = await db.MentorWeeklySlots
            .Where(w => w.MentorId == mentorId)
            .ToListAsync(cancellationToken);

        db.MentorWeeklySlots.RemoveRange(existing);

        foreach (var (dayOfWeek, startTime, endTime) in parsed)
        {
            db.MentorWeeklySlots.Add(new MentorWeeklySlot
            {
                MentorId = mentorId,
                DayOfWeek = dayOfWeek,
                StartTime = startTime,
                EndTime = endTime,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await SyncMentorSlotsWithWeeklyScheduleAsync(mentorId, closeUnmatchedOpenSlots: true, cancellationToken);
    }

    private Task EnsureUpcomingWeeklySlotsAsync(int mentorId, CancellationToken cancellationToken) =>
        SyncMentorSlotsWithWeeklyScheduleAsync(mentorId, closeUnmatchedOpenSlots: false, cancellationToken);

    private async Task SyncMentorSlotsWithWeeklyScheduleAsync(
        int mentorId,
        bool closeUnmatchedOpenSlots,
        CancellationToken cancellationToken)
    {
        var mentor = await db.MentorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MentorId == mentorId, cancellationToken);

        if (mentor is null)
        {
            return;
        }

        var weeklySlots = await db.MentorWeeklySlots
            .AsNoTracking()
            .Where(w => w.MentorId == mentorId)
            .OrderBy(w => w.DayOfWeek)
            .ThenBy(w => w.StartTime)
            .ToListAsync(cancellationToken);

        if (weeklySlots.Count == 0 && !closeUnmatchedOpenSlots)
        {
            return;
        }

        var nowUtc = clock.GetUtcNow();
        var localNow = ReportingPeriod.LocalInstant(nowUtc);
        var localToday = DateOnly.FromDateTime(localNow.DateTime);
        var currentIsoDay = ((int)localNow.DayOfWeek + 6) % 7 + 1;

        var expectedOccurrences = new List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>(
            mentor.IsActive ? weeklySlots.Count : 0);
        if (mentor.IsActive)
        {
            foreach (var w in weeklySlots)
            {
                var daysAhead = (w.DayOfWeek - currentIsoDay + 7) % 7;
                var candidateDate = localToday.AddDays(daysAhead);
                var candidateStartLocal = new DateTimeOffset(candidateDate.ToDateTime(w.StartTime), ReportingPeriod.UtcOffset);
                var candidateEndLocal = new DateTimeOffset(candidateDate.ToDateTime(w.EndTime), ReportingPeriod.UtcOffset);

                if (daysAhead == 0 && candidateStartLocal <= localNow)
                {
                    candidateDate = localToday.AddDays(7);
                    candidateStartLocal = new DateTimeOffset(candidateDate.ToDateTime(w.StartTime), ReportingPeriod.UtcOffset);
                    candidateEndLocal = new DateTimeOffset(candidateDate.ToDateTime(w.EndTime), ReportingPeriod.UtcOffset);
                }

                expectedOccurrences.Add((candidateStartLocal.ToUniversalTime(), candidateEndLocal.ToUniversalTime()));
            }
        }

        var futureSlots = await db.MentorSlots
            .Where(s => s.MentorId == mentorId && s.StartAt > nowUtc)
            .ToListAsync(cancellationToken);

        var openCode = SlotStatus.Open.ToCode();
        var closedCode = SlotStatus.Closed.ToCode();
        var changed = false;

        if (closeUnmatchedOpenSlots || weeklySlots.Count > 0)
        {
            foreach (var slot in futureSlots.Where(s => s.Status == openCode))
            {
                var matchesExpected = expectedOccurrences.Any(e =>
                    e.StartUtc == slot.StartAt.ToUniversalTime() &&
                    e.EndUtc == slot.EndAt.ToUniversalTime());

                if (!matchesExpected)
                {
                    slot.Status = closedCode;
                    changed = true;
                }
            }
        }

        if (mentor.IsActive)
        {
            foreach (var (startUtc, endUtc) in expectedOccurrences)
            {
                var existingSlot = futureSlots.FirstOrDefault(s =>
                    s.StartAt.ToUniversalTime() == startUtc &&
                    s.EndAt.ToUniversalTime() == endUtc);

                if (existingSlot is not null)
                {
                    if (existingSlot.Status == closedCode)
                    {
                        existingSlot.Status = openCode;
                        changed = true;
                    }
                    continue;
                }

                var overlapsActive = futureSlots.Any(s =>
                    s.Status != closedCode &&
                    s.StartAt < endUtc &&
                    s.EndAt > startUtc);

                if (!overlapsActive)
                {
                    var newSlot = new MentorSlot
                    {
                        MentorId = mentorId,
                        StartAt = startUtc,
                        EndAt = endUtc,
                        Status = openCode,
                    };
                    db.MentorSlots.Add(newSlot);
                    futureSlots.Add(newSlot);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<MentorWeeklySlotDto> ToWeeklySlotDtos(IEnumerable<MentorWeeklySlot> weeklySlots) =>
        weeklySlots
            .OrderBy(w => w.DayOfWeek)
            .ThenBy(w => w.StartTime)
            .Select(w => new MentorWeeklySlotDto(
                w.WeeklySlotId,
                w.MentorId,
                w.DayOfWeek,
                w.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                w.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture)))
            .ToList();
}
