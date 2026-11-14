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
                new NextActionDto(
                    dashboard.NextAction.Eyebrow,
                    dashboard.NextAction.Title,
                    dashboard.NextAction.Description,
                    dashboard.NextAction.ActionLabel,
                    dashboard.NextAction.ActionUrl,
                    dashboard.NextAction.Severity,
                    dashboard.NextAction.CascadeStep,
                    dashboard.NextAction.MetaLabel),
                // #241: the stored ΔH, the overall floor and its limiting skill all travel as the
                // service produced them. The API maps; it never recomputes a level or a verdict.
                new GoalStandingDto(
                    dashboard.Goal.CurrentBandCode,
                    dashboard.Goal.CurrentBandName,
                    dashboard.Goal.CurrentScore,
                    dashboard.Goal.CurrentSource,
                    dashboard.Goal.TargetBandCode,
                    dashboard.Goal.TargetBandName,
                    dashboard.Goal.TargetMinScore,
                    dashboard.Goal.TargetDate,
                    dashboard.Goal.WeeklyStudyHours,
                    dashboard.Goal.Status,
                    dashboard.Goal.CompletedPathSteps,
                    dashboard.Goal.TotalPathSteps,
                    dashboard.Goal.CurrentWeekNumber,
                    dashboard.Goal.WeekPlannedMinutes,
                    dashboard.Goal.WeekCompletedMinutes,
                    dashboard.Goal.ScoreDisplayFactor,
                    dashboard.Goal.RequiredTotalHours,
                    dashboard.Standing?.Overall?.Code,
                    dashboard.Standing?.LimitingSkillCode,
                    dashboard.Standing?.LimitingSkillName,
                    dashboard.IsUnplaced),
                // #241 / #211: a skill's level is null when nothing measured it. It is never filled
                // with the bottom band here - this mapping has no opinion about a level at all.
                dashboard.Skills.Select(s => new SkillCardDto(
                    s.SkillId,
                    s.Code,
                    s.Name,
                    s.Modality.MapTo<ModalityDto>(),
                    s.LatestScaledScore,
                    s.MaxScaledScore,
                    s.WeakestTopics.Select(c => new SkillCategoryDto(
                        c.Code, c.Name, c.AccuracyPercent, c.Answered, c.LevelCode)).ToList(),
                    s.ScoreDisplayFactor,
                    s.Standing?.Level?.Code,
                    s.Standing?.PositionInBand,
                    s.Standing?.MeasuredAt,
                    s.Standing?.IsStale ?? false)).ToList(),
                dashboard.AttemptCount,
                dashboard.PracticeCount,
                dashboard.UnfinishedAttempt is { } ua
                    ? new RecentAttemptDto(ua.AttemptId, ua.ExamName, ua.Purpose.MapTo<ExamPurposeDto>(), ua.Status.MapTo<AttemptStatusDto>(), ua.StartedAt, ua.TotalScore, ua.BandCode)
                    : null,
                dashboard.UnfinishedPractice is { } up
                    ? new RecentPracticeDto(up.SessionId, up.SectionName, up.Status.MapTo<PracticeStatusDto>(), up.CorrectCount, up.QuestionCount, up.StartedAt)
                    : null,
                dashboard.UpcomingBookings?.Select(b => new UpcomingMentorBookingDto(
                    b.BookingId,
                    b.MentorId,
                    b.MentorName,
                    b.CertCode,
                    b.SkillName,
                    b.StartAt,
                    b.EndAt,
                    b.MeetLink,
                    b.FocusArea)).ToList()));
        });
    }
}
