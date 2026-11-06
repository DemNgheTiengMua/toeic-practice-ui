using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #211 / D15: the one writer of a learner's per-skill state, and the one reader that computes the
/// overall.
/// <para>
/// <b>Why one type does both.</b> The overall is MIN over the measured skills. Computing it here, from
/// the same rows this type writes, is what makes TRN-13's second identity true by construction: there
/// is no stored overall for a second writer to disagree with, and no second implementation of MIN for
/// a screen to get subtly different.
/// </para>
/// <para>
/// <b>The write refuses rather than repairs.</b> A caller that hands in a skill outside the
/// certificate, a position outside 0-100, or a coordinate outside 1.00-6.99 gets an exception, not a
/// clamped value. Clamping is how a fabricated number reaches a screen: 140 silently becomes 100 and
/// reads as "top of the band".
/// </para>
/// </summary>
public sealed class SkillStateWriter(CompanionDbContext db, TimeProvider? clock = null) : ISkillStateWriter
{
    /// <summary>D18: a measured result may be reused for 90 calendar days.</summary>
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromDays(90);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// Records one measured skill and returns the learner's standing afterwards.
    /// <para>
    /// <b>The version moves forward on every write</b>, so an out-of-order callback - a placement
    /// result arriving after a calibration the learner already confirmed - cannot overwrite the newer
    /// state. The trigger refuses the attempt; this is what makes the refusal a legitimate refusal
    /// rather than the owner colliding with itself.
    /// </para>
    /// </summary>
    public async Task<LearnerStanding> RecordAsync(
        int userId,
        int certId,
        int skillId,
        CefrLevel level,
        int positionInBand,
        decimal continuousCoordinate,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (positionInBand is < 0 or > 100)
        {
            throw new SkillStateException(
                SkillStateException.InvalidPosition,
                $"Position in band must be 0-100; {positionInBand} is not a position inside a band.");
        }

        if (continuousCoordinate is < 1.00m or > 6.99m)
        {
            throw new SkillStateException(
                SkillStateException.OutsideScale,
                $"Continuous coordinate must be 1.00-6.99; {continuousCoordinate} is outside the scale.");
        }

        if (!IsKnownSource(source))
        {
            throw new SkillStateException(
                SkillStateException.UnknownSource,
                $"'{source}' is not a measurement source. A result comes from placement, mock_section or calibration.");
        }

        // The certificate must examine this skill, and the check is the same composite the foreign key
        // enforces. Doing it here as well turns a database exception into a message that names the
        // certificate and the skill, which is what a caller needs to debug it.
        var examed = await db.Skills
            .AsNoTracking()
            .AnyAsync(s => s.SkillId == skillId && s.CertId == certId, cancellationToken);

        if (!examed)
        {
            throw new SkillStateException(
                SkillStateException.SkillNotInCertificate,
                $"Skill {skillId} does not belong to certificate {certId}.");
        }

        var measuredAt = _clock.GetUtcNow();
        var row = await db.LearnerSkillStates
            .FirstOrDefaultAsync(
                s => s.UserId == userId && s.CertId == certId && s.SkillId == skillId,
                cancellationToken);

        if (row is null)
        {
            row = new LearnerSkillState
            {
                UserId = userId,
                CertId = certId,
                SkillId = skillId,
            };
            db.LearnerSkillStates.Add(row);
        }

        row.CefrLevel = level;
        row.PositionInBand = positionInBand;
        row.ContinuousCoordinate = continuousCoordinate;
        row.Source = source;
        row.MeasuredAt = measuredAt;
        row.ExpiresAt = measuredAt + ReuseWindow;
        // #191: the declaration must be IN this statement. EF omits an unchanged column from the
        // UPDATE, and the trigger checks UPDATE(WrittenByService), so the property is marked modified
        // explicitly - the convention PathStepCompletion already follows.
        row.WrittenByService = DerivedFactWriters.LearnerSkillState;
        db.Entry(row).Property(s => s.WrittenByService).IsModified = true;
        row.ProjectionVersion++;

        await db.SaveChangesAsync(cancellationToken);

        return await ReadAsync(userId, certId, cancellationToken)
            ?? throw new InvalidOperationException(
                "The per-skill state was written but cannot be read back, which means the write did not take effect.");
    }

    /// <summary>
    /// The learner's standing in a certificate: one entry per MEASURED skill, in the certificate's own
    /// order, with the overall and its limiting skill computed. Returns null when the certificate does
    /// not exist, because answering with an empty vector would read as "measured nothing" for a
    /// certificate that is not there.
    /// </summary>
    public async Task<LearnerStanding?> ReadAsync(
        int userId, int certId, CancellationToken cancellationToken = default)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code })
            .ToListAsync(cancellationToken);

        if (skills.Count == 0)
        {
            return null;
        }

        var stored = await db.LearnerSkillStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CertId == certId)
            .ToListAsync(cancellationToken);

        var bySkill = stored.ToDictionary(s => s.SkillId);
        var now = _clock.GetUtcNow();

        // The certificate's order is preserved, because that is the order the overall's tie-break
        // reads. A skill with no stored row is absent from this list, never present at A1.
        var measured = skills
            .Where(s => bySkill.ContainsKey(s.SkillId))
            .Select(s =>
            {
                var row = bySkill[s.SkillId];
                return new SkillStanding(
                    s.SkillId, s.Code, row.CefrLevel, row.PositionInBand,
                    row.ContinuousCoordinate, row.Source, row.MeasuredAt, row.ExpiresAt,
                    IsStale: now >= row.ExpiresAt);
            })
            .ToList();

        if (measured.Count == 0)
        {
            return new LearnerStanding(certId, measured, Overall: null, null, null);
        }

        // MIN over the measured skills, then the LOWEST position - the rule D5 applies to a placement's
        // LimitingSkillId, so the two surfaces cannot name different skills for the same data. Two
        // skills at B1 are not equally weak: 5/100 is one answer from dropping to A2.
        //
        // `ThenBy` on the level rank is technically redundant after the level comparison, but it makes
        // the ordering self-describing. The final tie-break is the certificate's order, which `measured`
        // already carries: a position tie between two skills must resolve the same way on every run.
        var weakest = measured
            .OrderBy(s => s.Level.Rank)
            .ThenBy(s => s.PositionInBand)
            .First();

        return new LearnerStanding(certId, measured, weakest.Level, weakest.SkillId, weakest.SkillCode);
    }

    private static bool IsKnownSource(string source) =>
        source is LearnerSkillStateSource.Placement
            or LearnerSkillStateSource.MockSection
            or LearnerSkillStateSource.Calibration;
}
