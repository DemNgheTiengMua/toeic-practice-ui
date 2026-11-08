using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapFreeDrill(RouteGroupBuilder practice)
    {
        // #235 / PLC-61..64: whether free drilling is open, which coordinates the plan prioritises, and
        // which lock is holding it. Read-only — the screen renders a lock, it does not create one.
        practice.MapGet("/free-drill", async (
            HttpContext http, IPracticeService practiceService, int? topicId, string? cefrLevelCode,
            CancellationToken cancellationToken) =>
        {
            CefrLevel? level = cefrLevelCode is not null && CefrLevel.TryParseAuthored(cefrLevelCode, out var parsed)
                ? parsed
                : null;

            var state = await practiceService.GetFreeDrillStateAsync(CurrentUserId(http), topicId, level, cancellationToken);

            return Results.Ok(new FreeDrillStateDto(
                state.IsUnlocked,
                state.IncompleteSteps
                    .Select(s => new IncompleteStepDto(s.StepId, s.WeekNumber, s.Title, s.TopicCode, s.EstimatedMinutes))
                    .ToList(),
                state.RemainingMinutes,
                state.Priorities
                    .Select(p => new PrioritisedCoordinateDto(
                        p.SkillId, p.SkillCode, p.SkillName, p.Modality.MapTo<ModalityDto>(),
                        p.TopicId, p.TopicCode, p.TopicName, p.Level.Code, p.Reason))
                    .ToList(),
                state.CellUsedToday,
                state.DailyCeilingReached,
                state.AnsweredToday,
                state.DailyItemCeiling,
                state.LockNotice));
        });
    }
}
