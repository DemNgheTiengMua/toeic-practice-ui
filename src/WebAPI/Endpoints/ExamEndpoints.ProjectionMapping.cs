using Contracts;
using BusinessObjects;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    /// <summary>
    /// #222: the mapping keeps the two quantities apart all the way to the wire. The certificate score
    /// arrives as <see cref="MockResultProjectionDto.CertificateScore"/> and the projected level only
    /// ever as a per-skill code plus the weakest projected one - there is no field where a total could
    /// be read as a level.
    /// </summary>
    private static MockResultProjectionDto ToMockResultProjectionDto(MockResultProjection p) =>
        new(
            p.AttemptId,
            p.CertId,
            p.CertificateScore,
            p.BandCode,
            p.BandName,
            p.ScoreDisplayFactor,
            p.Projected?.Skills
                .Select(s => new MockSkillProjectionDto(
                    s.SkillId,
                    s.ScaledScore,
                    s.AwardedLevel?.Code,
                    s.PositionInBand,
                    s.Outcome switch
                    {
                        ProjectionOutcome.Projected => MockProjectionOutcomeDto.Projected,
                        ProjectionOutcome.Uncertified => MockProjectionOutcomeDto.Uncertified,
                        ProjectionOutcome.DisplayCeiling => MockProjectionOutcomeDto.DisplayCeiling,
                        _ => MockProjectionOutcomeDto.CannotJudge,
                    },
                    s.CannotJudgeReason))
                .ToList()
                ?? [],
            p.Projected?.WeakestProjectedLevel?.Code,
            p.Projected?.WeakestProjectedSkillId,
            p.Stored?.Overall?.Code,
            p.Stored?.LimitingSkillCode,
            p.Stored?.HasNoMeasurement ?? true,
            p.Projected?.SkillIdsDifferingFrom(p.Stored) ?? [],
            p.SyncOffered,
            p.Historical,
            p.Projected?.CannotJudgeReason,
            p.Projected?.CutScoreTableVersion);

    private static MockSyncResultDto ToMockSyncResultDto(LearnerStanding standing) =>
        new(
            standing.CertId,
            standing.Overall?.Code,
            standing.LimitingSkillId,
            standing.LimitingSkillCode,
            standing.Skills
                .Select(s => new MockSyncSkillDto(
                    s.SkillId, s.SkillCode, s.Level.Code, s.PositionInBand, s.Source))
                .ToList());
}
