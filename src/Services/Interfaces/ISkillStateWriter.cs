using BusinessObjects;

namespace Services;

/// <summary>
/// #211 / D15: the one writer of a learner's per-skill state, and the read that computes the overall.
/// </summary>
public interface ISkillStateWriter
{
    /// <summary>Records one measured skill, written by <see cref="DerivedFactWriters.LearnerSkillState"/>.</summary>
    Task<LearnerStanding> RecordAsync(
        int userId,
        int certId,
        int skillId,
        CefrLevel level,
        int positionInBand,
        decimal continuousCoordinate,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The learner's standing in a certificate: the measured skills in the certificate's own order,
    /// with the overall (the weakest measured skill) and the limiting skill named.
    /// </summary>
    Task<LearnerStanding?> ReadAsync(int userId, int certId, CancellationToken cancellationToken = default);
}
