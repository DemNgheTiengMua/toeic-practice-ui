using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public static class LearnerStandingReader
{
    private static readonly TimeSpan ReuseWindow = TimeSpan.FromDays(90);

    public static async Task<LearnerStanding?> ReadAsync(
        CompanionDbContext db,
        int userId,
        int certId,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId
                        && p.Status == PlacementStatus.Done.ToCode()
                        && (p.CertId == certId || p.CertId == null))
            .OrderByDescending(p => p.CertId == certId)
            .ThenByDescending(p => p.TakenAt)
            .ThenByDescending(p => p.PlacementId)
            .Select(p => new
            {
                p.TakenAt,
                p.ListeningPct,
                p.ReadingPct,
                p.SpeakingPct,
                p.WritingPct
            })
            .FirstOrDefaultAsync(cancellationToken);

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Modality })
            .ToListAsync(cancellationToken);

        // A certificate with no skills does not exist as far as a standing is concerned; an empty
        // vector there would read as "measured nothing" for a certificate that is not there.
        if (skills.Count == 0)
        {
            return null;
        }

        if (placement is null)
        {
            return new LearnerStanding(certId, new List<SkillStanding>(), null, null, null);
        }

        var now = clock.GetUtcNow();
        var expiresAt = placement.TakenAt + ReuseWindow;
        var isStale = expiresAt < now;

        var list = new List<SkillStanding>();
        foreach (var skill in skills)
        {
            decimal? pct = skill.Modality.ToLowerInvariant() switch
            {
                "listening" => placement.ListeningPct,
                "reading" => placement.ReadingPct,
                "speaking" => placement.SpeakingPct,
                "writing" => placement.WritingPct,
                _ => null
            };

            if (pct is not { } skillPct)
            {
                continue;
            }

            var level = skillPct switch
            {
                >= 85m => CefrLevel.C1,
                >= 70m => CefrLevel.B2,
                >= 55m => CefrLevel.B1,
                >= 40m => CefrLevel.A2,
                >= 20m => CefrLevel.A1,
                _ => CefrLevel.PreA1,
            };
            var position = (int)Math.Clamp(Math.Round(skillPct), 0, 100);
            var coordinate = Math.Clamp(Math.Round(1.00m + (skillPct / 100m) * 5.99m, 2), 1.00m, 6.99m);

            list.Add(new SkillStanding(
                skill.SkillId,
                skill.Code,
                level,
                position,
                coordinate,
                "placement",
                placement.TakenAt,
                expiresAt,
                isStale));
        }

        if (list.Count == 0)
        {
            return new LearnerStanding(certId, list, null, null, null);
        }

        var weakest = list
            .OrderBy(s => s.Level)
            .ThenBy(s => s.PositionInBand)
            .First();

        return new LearnerStanding(certId, list, weakest.Level, weakest.SkillId, weakest.SkillCode);
    }
}
