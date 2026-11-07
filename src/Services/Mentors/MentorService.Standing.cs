using BusinessObjects;
using Contracts;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class MentorService
{
    /// <summary>
    /// CK_Skills_Modality's closed set, in the order a four-skill certificate lists them. A
    /// certificate that does not examine one of these still owes the mentor a statement about it, and
    /// this is the only list that can say which modalities exist at all.
    /// </summary>
    private static readonly string[] AllModalities =
        [Modality.Listening.ToCode(), Modality.Reading.ToCode(), Modality.Speaking.ToCode(), Modality.Writing.ToCode()];

    /// <summary>
    /// #223 / D21: the standing a mentor receives, derived at read time.
    /// <para>
    /// <b>Three absences, three statements.</b> Nothing measured at all is <c>HasNoMeasurement</c> - the
    /// learner owes a placement. A skill the certificate examines but the learner has no row for is
    /// <see cref="MentorSkillStandingStatus.NotMeasured"/>, because #211 keeps an unmeasured skill
    /// absent rather than present at A1. A modality the certificate has no skill for is
    /// <see cref="MentorSkillStandingStatus.NotExamined"/>, which no amount of sitting will fill.
    /// </para>
    /// <para>
    /// <b>No band is read here.</b> Every level comes from <see cref="ISkillStateWriter.ReadAsync"/>,
    /// which reads <c>LearnerSkillStates</c>; <c>ScoreBands</c> is not consulted, so a band code that
    /// happens to read "B2" cannot become a level and a C2 skill on a certificate whose bands stop at C1
    /// still reports C2.
    /// </para>
    /// </summary>
    private async Task<MentorLearnerStandingDto?> BuildStandingAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var certCode = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
        if (certCode is null)
        {
            return null;
        }

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Name, s.Modality })
            .ToListAsync(cancellationToken);

        // A certificate with no skills configured cannot be described. Answering with four
        // not-examined rows would read as "this certificate tests nothing", which is a content defect
        // and not a fact about the learner.
        if (skills.Count == 0)
        {
            return null;
        }

        var stored = await _skillStates.ReadAsync(userId, certId, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var measured = stored.Skills.ToDictionary(s => s.SkillId);
        var rows = new List<MentorSkillStandingDto>(skills.Count + AllModalities.Length);

        // The certificate's own order, the same order every other screen reads (#149).
        foreach (var skill in skills)
        {
            rows.Add(measured.TryGetValue(skill.SkillId, out var standing)
                ? new MentorSkillStandingDto(
                    skill.SkillId, skill.Modality, skill.Code, skill.Name,
                    MentorSkillStandingStatus.Measured, standing.Level.Code, standing.PositionInBand, standing.IsStale)
                : new MentorSkillStandingDto(
                    skill.SkillId, skill.Modality, skill.Code, skill.Name, MentorSkillStandingStatus.NotMeasured));
        }

        // Then one row per modality the certificate does not examine: no skill id, no code, no name,
        // and no level. Inventing a skill row for it is how the table would look complete and lie.
        var examined = skills.Select(s => s.Modality).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var modality in AllModalities.Where(m => !examined.Contains(m)))
        {
            rows.Add(new MentorSkillStandingDto(
                null, modality, null, null, MentorSkillStandingStatus.NotExamined));
        }

        return new MentorLearnerStandingDto(
            certId,
            certCode,
            stored.HasNoMeasurement,
            stored.Overall?.Code,
            stored.LimitingSkillId,
            stored.LimitingSkillCode,
            rows);
    }

    /// <summary>
    /// One standing per (learner, certificate) for a list of bookings: a learner with twelve bookings
    /// on one certificate costs two reads, not twenty-four.
    /// </summary>
    private async Task<Dictionary<(int UserId, int CertId), MentorLearnerStandingDto>> BuildStandingsAsync(
        IEnumerable<Booking> bookings, CancellationToken cancellationToken)
    {
        var standings = new Dictionary<(int, int), MentorLearnerStandingDto>();
        foreach (var (userId, certId) in bookings.Select(b => (b.UserId, b.Mentor.CertId)).Distinct())
        {
            if (await BuildStandingAsync(userId, certId, cancellationToken) is { } standing)
            {
                standings[(userId, certId)] = standing;
            }
        }

        return standings;
    }
}
