using System.ComponentModel.DataAnnotations;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearnerEndpoints
{
    private static void MapDashboard(RouteGroupBuilder learner)
    {

        learner.MapGet("/dashboard", async (HttpContext http, ILearnerService learners, CancellationToken cancellationToken) =>
        {
            var dashboard = await learners.GetDashboardAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new LearnerDashboardDto(
                ToCertificateDto(dashboard.Certificate),
                dashboard.LatestResult is { } latest
                    ? new LatestResultDto(latest.TotalScore, latest.BandCode, latest.BandName, latest.TakenAt)
                    : null,
                dashboard.IsNew,
                dashboard.AttemptCount,
                dashboard.PracticeCount,
                dashboard.RecentAttempts.Select(a => new RecentAttemptDto(
                    a.AttemptId, a.ExamName, a.Purpose.MapTo<ExamPurposeDto>(), a.Status.MapTo<AttemptStatusDto>(), a.StartedAt, a.TotalScore, a.BandCode)).ToList(),
                dashboard.RecentPractice.Select(p => new RecentPracticeDto(
                    p.SessionId, p.SectionName, p.Status.MapTo<PracticeStatusDto>(), p.CorrectCount, p.QuestionCount, p.StartedAt)).ToList(),
                dashboard.UnfinishedAttempt is { } ua
                    ? new RecentAttemptDto(ua.AttemptId, ua.ExamName, ua.Purpose.MapTo<ExamPurposeDto>(), ua.Status.MapTo<AttemptStatusDto>(), ua.StartedAt, ua.TotalScore, ua.BandCode)
                    : null,
                dashboard.UnfinishedPractice is { } up
                    ? new RecentPracticeDto(up.SessionId, up.SectionName, up.Status.MapTo<PracticeStatusDto>(), up.CorrectCount, up.QuestionCount, up.StartedAt)
                    : null,
                dashboard.TargetReadiness is { } tr
                    ? new TargetReadinessDto(
                        tr.CurrentBandCode,
                        tr.CurrentBandName,
                        tr.CurrentTotalScore,
                        tr.CurrentSource,
                        tr.TargetBandCode,
                        tr.TargetBandName,
                        tr.TargetMinScore,
                        tr.CompletedPathSteps,
                        tr.TotalPathSteps)
                    : null,
                dashboard.MissionSteps.Select(m => new MissionStepDto(
                    m.StepNumber,
                    m.BadgeLabel,
                    m.Severity,
                    m.Eyebrow,
                    m.Title,
                    m.Description,
                    m.MetaLabel,
                    m.ActionLabel,
                    m.ActionUrl,
                    m.IsCleared)).ToList(),
                dashboard.SimulatorConfig is { } sim
                    ? new SimulatorConfigDto(
                        sim.MaxTotalScore,
                        sim.CurrentTotalScore,
                        sim.CurrentBandCode,
                        sim.TargetBandCode,
                        sim.TargetMinScore,
                        sim.Skills.Select(s => new SimulatorSkillDto(
                            s.SkillId,
                            s.SkillCode,
                            s.SkillName,
                            s.Modality,
                            s.BaseScaledScore,
                            s.MinScaledScore,
                            s.MaxScaledScore,
                            s.Step)).ToList(),
                        sim.Bands.Select(b => new ScoreBandThresholdDto(
                            b.Code,
                            b.Name,
                            b.MinTotal,
                            b.MaxTotal)).ToList())
                    : null,
                dashboard.WeaknessStrips.Select(w => new WeaknessStripDto(
                    w.Scope,
                    w.Id,
                    w.Code,
                    w.Name,
                    w.SectionName,
                    w.Answered,
                    w.Correct,
                    w.AccuracyPercent,
                    w.Tier,
                    w.ActionLabel,
                    w.ActionUrl)).ToList(),
                dashboard.UpcomingBooking is { } ub
                    ? new UpcomingBookingCardDto(
                        ub.BookingId,
                        ub.MentorName,
                        ub.MentorHeadline,
                        ub.FocusArea,
                        ub.StartAt,
                        ub.EndAt,
                        ub.MeetLink)
                    : null,
                dashboard.LatestRubricHighlight is { } rh
                    ? new RubricHighlightDto(
                        rh.SkillName,
                        rh.CriterionName,
                        rh.Score,
                        rh.MaxScore,
                        rh.Comment,
                        rh.AttemptId,
                        rh.Purpose.MapTo<ExamPurposeDto>())
                    : null,
                dashboard.OverallAccuracyPercent));
        });
    }
}
